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
    /// <summary>Lote 246 — cuenta NO contable de QuickBooks Desktop donde viven las órdenes de compra (la crea QuickBooks al activar las órdenes de compra).</summary>
    public const string DefaultPurchaseOrderAccount = "Purchase Orders";

    /// <summary>Límites de QuickBooks Desktop: nombre completo de una cuenta, nombre de un ítem o de un término, nombre de un cliente.</summary>
    public const int MaxAccountLength = 159;
    public const int MaxItemLength = 31;
    public const int MaxTermLength = 31;
    public const int MaxCustomerLength = 41;
    /// <summary>Lote 246 — nombre de un proveedor (VEND) en QuickBooks Desktop.</summary>
    public const int MaxVendorLength = 41;
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

    public static string VendorNameTooLong(string name) =>
        $"El nombre del proveedor «{name}» pasa de {MaxVendorLength} caracteres: QuickBooks no lo acepta como nombre de proveedor.";

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

/// <summary>
/// Cuentas e ítems del archivo (ya con los valores por omisión aplicados). Lote 246: PurchaseOrders (la cuenta no contable de las órdenes de compra) va al final y con
/// valor por omisión, así que los new(...) de las facturas compilan igual.
/// </summary>
public sealed record QuickBooksAccounts(string Receivable, string Sales, string Cost, string Inventory, string TaxAccount, string TaxItem, string ShippingItem,
    string PurchaseOrders = QuickBooksRules.DefaultPurchaseOrderAccount)
{
    public static QuickBooksAccounts Defaults { get; } = new(QuickBooksRules.DefaultReceivableAccount, QuickBooksRules.DefaultSalesAccount, QuickBooksRules.DefaultCostAccount,
        QuickBooksRules.DefaultInventoryAccount, QuickBooksRules.DefaultTaxAccount, QuickBooksRules.DefaultTaxItem, QuickBooksRules.DefaultShippingItem,
        QuickBooksRules.DefaultPurchaseOrderAccount);
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

/// <summary>Lote 246 — proveedor nuevo para QuickBooks (VEND): nombre = nombre del proveedor en Teikem; Terms = nombre del término ya mapeado; Note = código o nota.</summary>
public sealed record QuickBooksVendor(string Name, string? CompanyName, string? Contact, string? Phone, string? Email, string? Terms, string? Note);

/// <summary>Lote 246 — línea de una orden de compra: ítem = SKU, descripción = nombre del producto, cantidad y costo unitario (positivos). Un kit va como el producto que es.</summary>
public sealed record QuickBooksPurchaseOrderLine(string ItemName, string? Description, decimal Quantity, decimal UnitCost);

/// <summary>Lote 246 — orden de compra (TRNS PURCHORD): número de Teikem, fecha, fecha esperada (DUEDATE), proveedor, memo (notas sin la marca de importación). Sin moneda.</summary>
public sealed record QuickBooksPurchaseOrder(string Number, DateOnly Date, DateOnly? ExpectedDate, string VendorName, string? Memo,
    IReadOnlyList<QuickBooksPurchaseOrderLine> Lines);

/// <summary>
/// Lote 225 — generador PURO del archivo IIF de QuickBooks Desktop: en este orden, los productos nuevos (!INVITEM), los clientes nuevos (!CUST) y las facturas
/// (!TRNS / !SPL / !ENDTRNS). Columnas separadas por tabulador, renglones con CRLF, fechas MM/DD/AAAA, montos con punto y 2 decimales. En las facturas el TRNS lleva el
/// total a cuentas por cobrar y cada SPL el ingreso en negativo (cantidad negativa), de modo que la transacción cuadra a cero; el IVU va en una SPL con el ítem de impuesto
/// (EXTRA = AUTOSTAX). Los textos se limpian: tabuladores y saltos de línea → espacio, comillas dobles → simples; en los NOMBRES (cliente, ítem) los dos puntos → guion
/// (en QuickBooks «A:B» es un subcliente o subítem). Las cuentas sí conservan los dos puntos («Sales:Monitors»).
/// <para>
/// Lote 246 — órdenes de compra (WritePurchaseOrders): productos nuevos (!INVITEM, el mismo bloque de las facturas), proveedores nuevos (!VEND) y las órdenes
/// (TRNS PURCHORD a la cuenta no contable «Purchase Orders» con el total en NEGATIVO; una SPL por línea a la cuenta de inventario con monto, cantidad y costo en
/// POSITIVO), de modo que cada transacción cuadra a 0.00 por construcción (TRNS = −Σ SPL ya redondeadas a centavos). DOCNUM = número de Teikem, DUEDATE = fecha
/// esperada. La moneda no viaja (QuickBooks la toma en la de la compañía) y los kits no se desglosan.
/// </para>
/// <para>
/// LIMITACIÓN CONOCIDA: el formato PURCHORD se construyó según la especificación IIF publicada por Intuit y NO se ha probado todavía contra un QuickBooks Desktop real
/// (falta la copia de la compañía que aportará el dueño; pendiente F-15). Si al importarlo QuickBooks lo rechaza o lo asienta distinto, lo primero que se ajusta son los
/// signos, las cuentas y la columna DUEDATE, y se ajustan SOLO aquí (WritePurchaseOrder y sus encabezados); las facturas no dependen de ese código.
/// </para>
/// </summary>
public static class QuickBooksIifWriter
{
    public const string ContentType = "application/octet-stream";
    private const string Nl = "\r\n";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string FileName(DateTime localNow) => $"teikem-quickbooks-{localNow:yyyyMMdd-HHmm}.iif";

    /// <summary>Lote 246 — nombre del archivo de órdenes de compra (con «compras» para no confundirlo con el de facturas).</summary>
    public static string PurchaseOrderFileName(DateTime localNow) => $"teikem-quickbooks-compras-{localNow:yyyyMMdd-HHmm}.iif";

    /// <summary>Texto en Windows-1252 (lo que lee QuickBooks Desktop); lo que no cabe sale como «?».</summary>
    public static byte[] Encode(string iif)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252).GetBytes(iif);
    }

    /// <summary>
    /// Lote 246 — los caracteres distintos de <paramref name="text"/> que <see cref="Encode"/> (Windows-1252) convierte en «?» (en el orden en que aparecen; un emoji
    /// cuenta como uno). Vacío = nada se pierde así. Sirve para el aviso no bloqueante de la exportación.
    /// </summary>
    public static string UnencodableChars(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp = Encoding.GetEncoding(1252);
        var seen = new HashSet<int>();
        var sb = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (!seen.Add(rune.Value)) continue;
            // Encode usa el «best fit» de Windows-1252 (p. ej. «ā» → «a»): eso no se avisa; solo lo que de verdad sale como «?».
            if (rune.Value == '?') continue;
            var bytes = cp.GetBytes(rune.ToString());
            if (bytes.Length > 0 && bytes.All(b => b == (byte)'?')) sb.Append(rune.ToString());
        }
        return sb.ToString();
    }

    public static string Write(QuickBooksAccounts acc, IReadOnlyList<QuickBooksItem> items, IReadOnlyList<QuickBooksCustomer> customers, IReadOnlyList<QuickBooksInvoice> invoices)
    {
        var sb = new StringBuilder();
        WriteItems(sb, acc, items);
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

    /// <summary>
    /// Lote 246 — archivo de órdenes de compra: productos nuevos (!INVITEM), proveedores nuevos (!VEND) y las órdenes (!TRNS / !SPL / !ENDTRNS con TRNSTYPE PURCHORD).
    /// Cada bloque sale solo si trae algo. No se ha probado aún contra QuickBooks real (ver el comentario de la clase).
    /// </summary>
    public static string WritePurchaseOrders(QuickBooksAccounts acc, IReadOnlyList<QuickBooksItem> items, IReadOnlyList<QuickBooksVendor> vendors,
        IReadOnlyList<QuickBooksPurchaseOrder> orders)
    {
        var sb = new StringBuilder();
        WriteItems(sb, acc, items);
        if (vendors.Count > 0)
        {
            Row(sb, "!VEND", "NAME", "COMPANYNAME", "CONT1", "PHONE1", "EMAIL", "TERMS", "NOTE");
            foreach (var v in vendors)
                Row(sb, "VEND", Name(v.Name), Text(v.CompanyName), Text(v.Contact), Text(v.Phone), Text(v.Email), Text(v.Terms), Text(v.Note));
        }
        if (orders.Count > 0)
        {
            Row(sb, "!TRNS", "TRNSID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "AMOUNT", "DOCNUM", "MEMO", "CLEAR", "TOPRINT", "DUEDATE");
            Row(sb, "!SPL", "SPLID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "AMOUNT", "DOCNUM", "MEMO", "CLEAR", "QNTY", "PRICE", "INVITEM");
            Row(sb, "!ENDTRNS");
            foreach (var po in orders) WritePurchaseOrder(sb, acc, po);
        }
        return sb.ToString();
    }

    /// <summary>Lote 246 — las líneas de una orden de compra como salen en el archivo: (ítem, descripción, cantidad, costo, monto = cantidad × costo a centavos).</summary>
    public static IReadOnlyList<(string Item, string? Description, decimal Quantity, decimal Cost, decimal Amount)> PurchaseOrderDetail(QuickBooksPurchaseOrder order)
        => order.Lines.Select(l => (l.ItemName, l.Description, l.Quantity, l.UnitCost, QuickBooksRules.LineAmount(l.Quantity, l.UnitCost))).ToList();

    /// <summary>Lote 246 — total de la orden en el archivo: la suma de los montos de línea YA redondeados (puede diferir del total de Teikem, que redondea al final).</summary>
    public static decimal PurchaseOrderFileTotal(QuickBooksPurchaseOrder order) => PurchaseOrderDetail(order).Sum(d => d.Amount);

    private static void WritePurchaseOrder(StringBuilder sb, QuickBooksAccounts acc, QuickBooksPurchaseOrder po)
    {
        var detail = PurchaseOrderDetail(po);
        var total = detail.Sum(d => d.Amount);
        var date = Date(po.Date);
        var number = Text(po.Number);
        Row(sb, "TRNS", string.Empty, "PURCHORD", date, Text(acc.PurchaseOrders), Name(po.VendorName), Money(-total), number, Text(po.Memo), "N", "N",
            po.ExpectedDate is DateOnly d ? Date(d) : string.Empty);
        foreach (var r in detail)
            Row(sb, "SPL", string.Empty, "PURCHORD", date, Text(acc.Inventory), string.Empty, Money(r.Amount), number, Text(r.Description), "N",
                PoQty(r.Quantity), Price(r.Cost), Name(r.Item));
        Row(sb, "ENDTRNS");
    }

    /// <summary>Bloque de productos nuevos (!INVITEM), el mismo para facturas y órdenes de compra.</summary>
    private static void WriteItems(StringBuilder sb, QuickBooksAccounts acc, IReadOnlyList<QuickBooksItem> items)
    {
        if (items.Count == 0) return;
        Row(sb, "!INVITEM", "NAME", "INVITEMTYPE", "DESC", "PURCHASEDESC", "ACCNT", "ASSETACCNT", "COGSACCNT", "PRICE", "COST", "TAXABLE");
        foreach (var i in items)
            Row(sb, "INVITEM", Name(i.Name), "INVENTORY", Text(i.Description), Text(i.Description), Text(acc.Sales), Text(acc.Inventory), Text(acc.Cost),
                Price(i.Price), Price(i.Cost), YesNo(i.Taxable));
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
    /// <summary>Cantidad de una orden de compra: positiva, hasta 4 decimales (los de PurchaseOrderLine).</summary>
    private static string PoQty(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero).ToString("0.####", Inv);
    private static string Date(DateOnly d) => d.ToString("MM/dd/yyyy", Inv);
    private static string YesNo(bool b) => b ? "Y" : "N";
}
