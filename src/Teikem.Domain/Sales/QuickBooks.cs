using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Teikem.Domain.Sales;

/// <summary>
/// Lote 225 (P5 del pedido de vendedores) — reglas de QuickBooks Desktop: valores por omisión de las cuentas e ítems del archivo IIF, límites de QuickBooks, el mapa de
/// términos y los mensajes exactos. Una factura tiene UNA sola marca «en QuickBooks» (sí/no); exportar no la marca (decisión de Luis, séptima ronda).
/// </summary>
public static class QuickBooksRules
{
    public const string DefaultReceivableAccount = "Accounts Receivable";
    public const string DefaultSalesAccount = "Sales";
    public const string DefaultCostAccount = "Cost of Goods Sold";
    public const string DefaultInventoryAccount = "Inventory Asset";
    public const string DefaultTaxAccount = "Sales Tax Payable";
    public const string DefaultTaxItem = "IVU";
    public const string DefaultShippingItem = "Shipping";

    /// <summary>Límites de QuickBooks Desktop: nombre completo de una cuenta, nombre de un ítem o de un término, nombre de un cliente.</summary>
    public const int MaxAccountLength = 159;
    public const int MaxItemLength = 31;
    public const int MaxTermLength = 31;
    public const int MaxCustomerLength = 41;
    /// <summary>Tope de facturas por llamada al marcar o confirmar por lista (para «todas» está all = true).</summary>
    public const int MaxIdsPerCall = 1000;

    public const string NothingPending = "No hay facturas por pasar a QuickBooks.";
    public const string SelectionRequired = "Indique las facturas (invoiceIds) o pida todas (all = true).";
    public const string TooManyIds = "No se pueden marcar más de 1000 facturas en una sola llamada; use «todas».";
    public const string InvoicesNotFound = "Alguna de las facturas indicadas no existe o no es una factura de venta emitida.";
    public const string AccountTooLong = "El nombre de una cuenta de QuickBooks no puede pasar de 159 caracteres.";
    public const string ItemTooLong = "El nombre de un ítem de QuickBooks no puede pasar de 31 caracteres.";
    public const string TermTooLong = "El nombre de un término de QuickBooks no puede pasar de 31 caracteres.";
    public const string BodyRequired = "El cuerpo de la solicitud es obligatorio.";

    public static string NotExported(string number) =>
        $"La factura {number} no ha salido en ningún archivo de QuickBooks; para marcarla a mano use «Marcar como en QuickBooks».";

    public static string UnknownTerm(string code) => $"El término de pago «{code}» no existe en el catálogo de términos de pago.";

    public static string CustomerNameTooLong(string name) =>
        $"El nombre del cliente «{name}» pasa de {MaxCustomerLength} caracteres: QuickBooks no lo acepta como nombre de cliente.";

    public static string ItemNameTooLong(string sku) =>
        $"El SKU «{sku}» pasa de {MaxItemLength} caracteres: QuickBooks no lo acepta como nombre de ítem.";

    public static string UnmappedTerm(string code) =>
        $"El término «{code}» no tiene nombre en QuickBooks (Ajustes → Operación → QuickBooks): esas facturas salen sin término.";

    /// <summary>Lo guardado o el valor por omisión (vacío = por omisión).</summary>
    public static string Effective(string? stored, string fallback) => string.IsNullOrWhiteSpace(stored) ? fallback : stored.Trim();

    /// <summary>Lee el mapa término → nombre en QuickBooks (claves sin distinguir mayúsculas). Un JSON dañado cuenta como vacío.</summary>
    public static IReadOnlyDictionary<string, string> ParseTerms(string? json)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return map;
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            if (raw is null) return map;
            foreach (var (k, v) in raw)
                if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v)) map[k.Trim().ToUpperInvariant()] = v.Trim();
        }
        catch (JsonException) { /* dañado = sin mapa */ }
        return map;
    }

    /// <summary>Guarda el mapa ordenado por código; vacío = NULL.</summary>
    public static string? SerializeTerms(IReadOnlyDictionary<string, string> map)
        => map.Count == 0 ? null : JsonSerializer.Serialize(map.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value));

    /// <summary>
    /// Reparte el monto de una línea de kit entre sus componentes a prorrata de los pesos (cantidad por kit × precio de lista) y CIERRA AL CENTAVO: cada parte se
    /// redondea a 2 decimales por el método del mayor residuo (los centavos que sobran van a las partes con mayor fracción; empate: la primera). Pesos todos en cero
    /// (componentes sin precio) = a partes iguales. El signo del monto se respeta.
    /// </summary>
    public static IReadOnlyList<decimal> Prorate(decimal amount, IReadOnlyList<decimal> weights)
    {
        if (weights.Count == 0) throw new ArgumentException("Un kit sin componentes no se puede desglosar.", nameof(weights));
        var w = weights.Select(x => x > 0m ? x : 0m).ToArray();
        var total = w.Sum();
        if (total <= 0m) { w = Enumerable.Repeat(1m, w.Length).ToArray(); total = w.Length; }
        var sign = amount < 0m ? -1m : 1m;
        var cents = (long)Math.Round(Math.Abs(amount) * 100m, 0, MidpointRounding.AwayFromZero);
        var raw = w.Select(x => cents * x / total).ToArray();
        var floor = raw.Select(r => (long)Math.Floor(r)).ToArray();
        var left = cents - floor.Sum();
        foreach (var i in Enumerable.Range(0, raw.Length).OrderByDescending(i => raw[i] - floor[i]).ThenBy(i => i).Take((int)left)) floor[i]++;
        return floor.Select(c => sign * c / 100m).ToArray();
    }

    /// <summary>Monto de una línea: cantidad × precio a 2 decimales (lo que QuickBooks asienta).</summary>
    public static decimal LineAmount(decimal quantity, decimal unitPrice) => Math.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Cuentas e ítems del archivo (ya con los valores por omisión aplicados).</summary>
public sealed record QuickBooksAccounts(string Receivable, string Sales, string Cost, string Inventory, string TaxAccount, string TaxItem, string ShippingItem)
{
    public static QuickBooksAccounts Defaults { get; } = new(QuickBooksRules.DefaultReceivableAccount, QuickBooksRules.DefaultSalesAccount, QuickBooksRules.DefaultCostAccount,
        QuickBooksRules.DefaultInventoryAccount, QuickBooksRules.DefaultTaxAccount, QuickBooksRules.DefaultTaxItem, QuickBooksRules.DefaultShippingItem);
}

/// <summary>Producto nuevo para QuickBooks (INVITEM de inventario): nombre = SKU de Teikem.</summary>
public sealed record QuickBooksItem(string Name, string? Description, decimal? Price, decimal? Cost, bool Taxable);

/// <summary>Cliente nuevo para QuickBooks (CUST): nombre = nombre del negocio; AccountNumber = número del cliente en Teikem; ResaleNumber = SURI.</summary>
public sealed record QuickBooksCustomer(string Name, string? AccountNumber, string? CompanyName, IReadOnlyList<string> BillTo, IReadOnlyList<string> ShipTo,
    string? Phone1, string? Phone2, string? Email, string? Contact, string? Terms, string? ResaleNumber, decimal? CreditLimit);

/// <summary>Una pieza de un kit: el ítem del componente, cuántas lleva cada kit y su precio de lista (el peso del prorrateo).</summary>
public sealed record QuickBooksKitPart(string ItemName, string? Description, decimal QuantityPerKit, decimal? ListPrice);

/// <summary>Línea de la factura. Con Parts (un kit) sale desglosada por componente con el monto prorrateado; el kit no viaja como ítem.</summary>
public sealed record QuickBooksInvoiceLine(string ItemName, string? Description, decimal Quantity, decimal UnitPrice, bool Taxable, IReadOnlyList<QuickBooksKitPart>? Parts = null);

/// <summary>Factura de venta (TRNS INVOICE). TaxAmount = IVU total de la factura (una línea con el ítem de IVU).</summary>
public sealed record QuickBooksInvoice(string Number, DateOnly Date, DateOnly? DueDate, string CustomerName, string? Terms, string? Memo,
    IReadOnlyList<QuickBooksInvoiceLine> Lines, decimal TaxAmount);

/// <summary>
/// Lote 225 — generador PURO del archivo IIF de QuickBooks Desktop: en este orden, los productos nuevos (!INVITEM), los clientes nuevos (!CUST) y las facturas
/// (!TRNS / !SPL / !ENDTRNS). Columnas separadas por tabulador, renglones con CRLF, fechas MM/DD/AAAA, montos con punto y 2 decimales. En las facturas el TRNS lleva el
/// total a cuentas por cobrar y cada SPL el ingreso en negativo (cantidad negativa), de modo que la transacción cuadra a cero; el IVU va en una SPL con el ítem de impuesto
/// (EXTRA = AUTOSTAX). Los textos se limpian: tabuladores y saltos de línea → espacio, comillas dobles → simples; en los NOMBRES (cliente, ítem) los dos puntos → guion
/// (en QuickBooks «A:B» es un subcliente o subítem). Las cuentas sí conservan los dos puntos («Sales:Monitors»).
/// </summary>
public static class QuickBooksIifWriter
{
    public const string ContentType = "application/octet-stream";
    private const string Nl = "\r\n";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string FileName(DateTime localNow) => $"teikem-quickbooks-{localNow:yyyyMMdd-HHmm}.iif";

    /// <summary>Texto en Windows-1252 (lo que lee QuickBooks Desktop); lo que no cabe sale como «?».</summary>
    public static byte[] Encode(string iif)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252).GetBytes(iif);
    }

    public static string Write(QuickBooksAccounts acc, IReadOnlyList<QuickBooksItem> items, IReadOnlyList<QuickBooksCustomer> customers, IReadOnlyList<QuickBooksInvoice> invoices)
    {
        var sb = new StringBuilder();
        if (items.Count > 0)
        {
            Row(sb, "!INVITEM", "NAME", "INVITEMTYPE", "DESC", "PURCHASEDESC", "ACCNT", "ASSETACCNT", "COGSACCNT", "PRICE", "COST", "TAXABLE");
            foreach (var i in items)
                Row(sb, "INVITEM", Name(i.Name), "INVENTORY", Text(i.Description), Text(i.Description), Text(acc.Sales), Text(acc.Inventory), Text(acc.Cost),
                    Price(i.Price), Price(i.Cost), YesNo(i.Taxable));
        }
        if (customers.Count > 0)
        {
            Row(sb, "!CUST", "NAME", "COMPANYNAME", "BADDR1", "BADDR2", "BADDR3", "BADDR4", "BADDR5", "SADDR1", "SADDR2", "SADDR3", "SADDR4", "SADDR5",
                "PHONE1", "PHONE2", "EMAIL", "CONT1", "TERMS", "LIMIT", "RESALENUM", "NOTE");
            foreach (var c in customers)
            {
                var cells = new List<string> { "CUST", Name(c.Name), Text(c.CompanyName) };
                cells.AddRange(Address(c.BillTo));
                cells.AddRange(Address(c.ShipTo));
                cells.AddRange(new[] { Text(c.Phone1), Text(c.Phone2), Text(c.Email), Text(c.Contact), Text(c.Terms), c.CreditLimit is decimal l ? Money(l) : string.Empty,
                    Text(c.ResaleNumber), Text(c.AccountNumber) });
                Row(sb, cells.ToArray());
            }
        }
        if (invoices.Count > 0)
        {
            Row(sb, "!TRNS", "TRNSID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "AMOUNT", "DOCNUM", "MEMO", "CLEAR", "TOPRINT", "NAMEISTAXABLE", "DUEDATE", "TERMS");
            Row(sb, "!SPL", "SPLID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "AMOUNT", "DOCNUM", "MEMO", "CLEAR", "QNTY", "PRICE", "INVITEM", "TAXABLE", "EXTRA");
            Row(sb, "!ENDTRNS");
            foreach (var inv in invoices) WriteInvoice(sb, acc, inv);
        }
        return sb.ToString();
    }

    /// <summary>Las líneas de detalle de una factura (sin el IVU), ya desglosados los kits: (ítem, descripción, cantidad, precio, monto, gravable).</summary>
    public static IReadOnlyList<(string Item, string? Description, decimal Quantity, decimal Price, decimal Amount, bool Taxable)> Detail(QuickBooksInvoice inv)
    {
        var rows = new List<(string, string?, decimal, decimal, decimal, bool)>();
        foreach (var line in inv.Lines)
        {
            var amount = QuickBooksRules.LineAmount(line.Quantity, line.UnitPrice);
            if (line.Parts is not { Count: > 0 } parts)
            {
                rows.Add((line.ItemName, line.Description, line.Quantity, line.UnitPrice, amount, line.Taxable));
                continue;
            }
            var shares = QuickBooksRules.Prorate(amount, parts.Select(p => p.QuantityPerKit * (p.ListPrice ?? 0m)).ToList());
            for (var i = 0; i < parts.Count; i++)
            {
                var qty = line.Quantity * parts[i].QuantityPerKit;
                var price = qty == 0m ? 0m : Math.Round(shares[i] / qty, 5, MidpointRounding.AwayFromZero);
                var desc = string.IsNullOrWhiteSpace(parts[i].Description) ? line.ItemName : $"{parts[i].Description} ({line.ItemName})";
                rows.Add((parts[i].ItemName, desc, qty, price, shares[i], line.Taxable));
            }
        }
        return rows;
    }

    private static void WriteInvoice(StringBuilder sb, QuickBooksAccounts acc, QuickBooksInvoice inv)
    {
        var detail = Detail(inv);
        var tax = Math.Round(inv.TaxAmount, 2, MidpointRounding.AwayFromZero);
        var total = detail.Sum(d => d.Amount) + tax;
        var date = Date(inv.Date);
        Row(sb, "TRNS", string.Empty, "INVOICE", date, Text(acc.Receivable), Name(inv.CustomerName), Money(total), Text(inv.Number), Text(inv.Memo), "N", "N",
            YesNo(tax != 0m), inv.DueDate is DateOnly d ? Date(d) : string.Empty, Text(inv.Terms));
        foreach (var r in detail)
            Row(sb, "SPL", string.Empty, "INVOICE", date, Text(acc.Sales), string.Empty, Money(-r.Amount), Text(inv.Number), Text(r.Description), "N",
                Qty(-r.Quantity), Price(r.Price), Name(r.Item), YesNo(r.Taxable), string.Empty);
        if (tax != 0m)
            Row(sb, "SPL", string.Empty, "INVOICE", date, Text(acc.TaxAccount), string.Empty, Money(-tax), Text(inv.Number), Text(acc.TaxItem), "N",
                string.Empty, string.Empty, Name(acc.TaxItem), "N", "AUTOSTAX");
        Row(sb, "ENDTRNS");
    }

    /// <summary>Cinco renglones de dirección (los que sobran, vacíos).</summary>
    private static IEnumerable<string> Address(IReadOnlyList<string> lines)
    {
        var clean = lines.Select(Text).Where(l => l.Length > 0).Take(5).ToList();
        while (clean.Count < 5) clean.Add(string.Empty);
        return clean;
    }

    private static void Row(StringBuilder sb, params string[] cells) => sb.Append(string.Join('\t', cells)).Append(Nl);

    /// <summary>Texto libre limpio: sin tabuladores ni saltos de línea (rompen las columnas), sin comillas dobles, espacios juntos y recortado.</summary>
    public static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        var space = false;
        foreach (var ch in value)
        {
            var c = ch is '\t' or '\r' or '\n' ? ' ' : ch == '"' ? '\'' : ch;
            if (char.IsControl(c)) c = ' ';
            if (c == ' ') { if (space) continue; space = true; } else space = false;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    /// <summary>Nombre de cliente o de ítem: como Text, y los dos puntos (separador de subcliente / subítem en QuickBooks) → guion.</summary>
    public static string Name(string? value) => Text(value).Replace(':', '-');

    public static string Money(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero).ToString("0.00", Inv);
    private static string Price(decimal? v) => v is decimal d ? d.ToString("0.00###", Inv) : string.Empty;
    private static string Qty(decimal v) => v.ToString("0.#####", Inv);
    private static string Date(DateOnly d) => d.ToString("MM/dd/yyyy", Inv);
    private static string YesNo(bool b) => b ? "Y" : "N";
}
