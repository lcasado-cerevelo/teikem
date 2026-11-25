using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Teikem.Domain.Sales;
using Teikem.Domain.Tenancy;

namespace Teikem.Infrastructure.Notifications;

// ======================================================================================================================
// Lote 243 (Lote G del plan de Solutions) — el correo con la orden de venta completa al solicitarla (como el de Formsite).
// Lógica PURA: el modelo ya viene armado por SalesOrderEmailContent (que lee la base); aquí solo se escribe el HTML y la línea
// de resumen, y se calcula la huella. Probado con xunit (SalesOrderEmailHtmlTests).
// ======================================================================================================================

/// <summary>Un componente de un kit en la línea: SKU y nombre de la pieza real, piezas por kit y total; SubstitutesSku = la pieza del kit que sustituye («en lugar de»).</summary>
public sealed record SalesOrderEmailComponent(string Sku, string Name, decimal QtyPerKit, decimal TotalQty, string? SubstitutesSku = null);

/// <summary>Una línea de la orden. Backordered = lo que quedó en backorder (solo lo ve el personal).</summary>
public sealed record SalesOrderEmailLine(int LineNo, string Sku, string Description, decimal Quantity, decimal UnitPrice, decimal Amount,
    bool IsTaxExempt = false, decimal Backordered = 0m, IReadOnlyList<SalesOrderEmailComponent>? Components = null);

/// <summary>Un renglón del impuesto ya con su etiqueta en el idioma del correo (p. ej. «IVU estatal», 10.5 %); IsExempt = se muestra «exento».</summary>
public sealed record SalesOrderEmailTax(string Label, decimal? Percent, decimal Amount, bool IsExempt = false);

/// <summary>Una fila de la tabla del correo: la línea o, si Component no es null, una subfila de su kit.</summary>
public readonly record struct SalesOrderEmailRow(SalesOrderEmailLine Line, SalesOrderEmailComponent? Component);

/// <summary>
/// Todo lo que lleva el correo de la orden, ya resuelto (etiquetas de catálogo en el idioma del correo). Audience = Customer omite el
/// enlace, las notas, las marcas internas (forma de pago cambiada, cliente nuevo por aprobar, faltante por línea, reautenticación) y el
/// aviso de que la orden cambió.
/// </summary>
public sealed record SalesOrderEmailModel
{
    public string Lang { get; init; } = "es";
    public NotificationAudience Audience { get; init; } = NotificationAudience.Staff;
    public TenantFormat Format { get; init; } = TenantFormatRules.Defaults(TenantFormatRules.PuertoRico);
    public string CompanyName { get; init; } = string.Empty;
    public string OrderNumber { get; init; } = string.Empty;
    public bool IsUrgent { get; init; }
    public DateOnly OrderDate { get; init; }
    /// <summary>Cuándo se solicitó (UTC); se muestra en la zona de la compañía.</summary>
    public DateTime? RequestedAtUtc { get; init; }
    public string? SellerName { get; init; }
    /// <summary>Quien la capturó, solo si es otra persona que el vendedor («Capturó: X»).</summary>
    public string? CapturedByName { get; init; }
    public string ClientName { get; init; } = string.Empty;
    public string? ClientCode { get; init; }
    public IReadOnlyList<string> ClientAddress { get; init; } = Array.Empty<string>();
    public string? ClientPhone { get; init; }
    public string? MerchantRegistration { get; init; }
    /// <summary>El cliente es un prospecto por aprobar (marca interna).</summary>
    public bool ClientPendingApproval { get; init; }
    /// <summary>DELIVERY (entrega al consignatario) o PICKUP (recogido en el almacén).</summary>
    public string DeliveryMode { get; init; } = SalesDeliveryModes.Pickup;
    public string? ConsigneeName { get; init; }
    public IReadOnlyList<string> ConsigneeAddress { get; init; } = Array.Empty<string>();
    public string WarehouseName { get; init; } = string.Empty;
    public string? PaymentMethod { get; init; }
    public string? PaymentTerm { get; init; }
    public bool PaymentChanged { get; init; }
    public bool IsConsignment { get; init; }
    public DateOnly? RequiredDate { get; init; }
    public string? Notes { get; init; }
    /// <summary>BIOMETRIC | PASSWORD (marca interna: cómo se reautenticó el vendedor al enviar).</summary>
    public string? SubmittedWithAuth { get; init; }
    public IReadOnlyList<SalesOrderEmailLine> Lines { get; init; } = Array.Empty<SalesOrderEmailLine>();
    public decimal Subtotal { get; init; }
    public decimal ShippingCharge { get; init; }
    /// <summary>Por qué lleva (o no) envío, ya en el idioma del correo; null = sin regla de envío.</summary>
    public string? ShippingReason { get; init; }
    public IReadOnlyList<SalesOrderEmailTax> Taxes { get; init; } = Array.Empty<SalesOrderEmailTax>();
    public decimal Total { get; init; }
    public string? AuthorizedByName { get; init; }
    public bool HasSignature { get; init; }
    /// <summary>Enlace absoluto a la orden en la web (http/https); null = sin botón.</summary>
    public string? Link { get; init; }
    /// <summary>La huella del aviso ya no coincide con la orden (cambió después de solicitarse).</summary>
    public bool ChangedSinceRequest { get; init; }

    public bool IsStaff => Audience == NotificationAudience.Staff;
    public bool IsEnglish => string.Equals(Lang, "en", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Reglas puras del correo de la orden: la huella y el tope de filas.</summary>
public static class SalesOrderEmailRules
{
    /// <summary>Tope de filas de la tabla (contando las subfilas de los kits); a partir de ahí, «… y N líneas más.».</summary>
    public const int MaxRows = 200;

    /// <summary>Tope de peso del HTML del correo (Gmail recorta a partir de 102 KB).</summary>
    public const int MaxHtmlBytes = 100 * 1024;

    public const string ChangedNoticeEs = "Esta orden cambió después de solicitarse; el correo muestra los datos vigentes.";
    public const string ChangedNoticeEn = "This order changed after it was requested; the email shows the current data.";

    /// <summary>
    /// Huella de la orden al solicitarla: 16 caracteres hexadecimales (minúsculas) del SHA-256 de (LineNo, ProductId, QtyOrdered,
    /// UnitPrice) de cada línea, en orden de LineNo, y del total. No depende de la escala del decimal (4 = 4.0000).
    /// </summary>
    public static string Fingerprint(IEnumerable<(int LineNo, int ProductId, decimal QtyOrdered, decimal UnitPrice)> lines, decimal total)
    {
        var sb = new StringBuilder();
        foreach (var l in lines.OrderBy(l => l.LineNo).ThenBy(l => l.ProductId))
            sb.Append(l.LineNo.ToString(CultureInfo.InvariantCulture)).Append('|').Append(l.ProductId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(Dec(l.QtyOrdered)).Append('|').Append(Dec(l.UnitPrice)).Append(';');
        sb.Append("T|").Append(Dec(total));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    private static string Dec(decimal v) => v.ToString("0.############", CultureInfo.InvariantCulture);

    /// <summary>¿La orden cambió? Solo si el aviso trae huella y no coincide (los avisos viejos no la traen: no se avisa).</summary>
    public static bool Changed(string? published, string current)
        => !string.IsNullOrWhiteSpace(published) && !string.Equals(published.Trim(), current, StringComparison.OrdinalIgnoreCase);

    /// <summary>Las filas de la tabla (cada línea y, debajo, los componentes de su kit), cortadas en <paramref name="max"/>; Omitted = las que no caben.</summary>
    public static (IReadOnlyList<SalesOrderEmailRow> Rows, int Omitted) Rows(IReadOnlyList<SalesOrderEmailLine> lines, int max = MaxRows)
    {
        var all = new List<SalesOrderEmailRow>();
        foreach (var l in lines.OrderBy(l => l.LineNo))
        {
            all.Add(new SalesOrderEmailRow(l, null));
            foreach (var c in l.Components ?? Array.Empty<SalesOrderEmailComponent>()) all.Add(new SalesOrderEmailRow(l, c));
        }
        return all.Count <= max ? (all, 0) : (all.Take(max).ToList(), all.Count - max);
    }
}

/// <summary>
/// El HTML del correo de la orden (apto para correo: estilos en línea, ancho máximo de 680 px y tablas al 100 % sin anchos fijos, sin
/// imágenes ni CSS externo) y la línea de resumen para la campanita y el SMS. TODO valor pasa por <see cref="Encode"/>.
/// </summary>
public static class SalesOrderEmailHtml
{
    private const string Muted = "color:#4b5563";

    /// <summary>Codifica un valor para HTML (texto y atributos): &amp; &lt; &gt; " y '. Los acentos quedan tal cual (el correo va en UTF-8).</summary>
    public static string Encode(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length + 16);
        foreach (var ch in value)
            sb.Append(ch switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => ch.ToString(),
            });
        return sb.ToString();
    }

    /// <summary>Etiquetas del correo en español e inglés.</summary>
    private static string L(SalesOrderEmailModel m, string key) => (m.IsEnglish ? En : Es).TryGetValue(key, out var v) ? v : key;

    private static readonly Dictionary<string, string> Es = new()
    {
        ["title"] = "Orden de venta", ["urgent"] = "URGENTE", ["orderDate"] = "Fecha de la orden", ["requestedAt"] = "Solicitada",
        ["seller"] = "Vendedor", ["captured"] = "Capturó", ["client"] = "Cliente", ["phone"] = "Tel.", ["merchant"] = "Registro de comerciante",
        ["newClient"] = "Cliente nuevo por aprobar", ["delivery"] = "Entrega", ["deliverTo"] = "Entrega a", ["pickupAt"] = "Recogido en",
        ["payment"] = "Forma de pago", ["paymentChanged"] = "forma de pago cambiada", ["consignment"] = "Consignación", ["required"] = "Fecha requerida",
        ["notes"] = "Notas", ["auth"] = "Reautenticación", ["authBiometric"] = "Enviada con huella o rostro", ["authPassword"] = "Enviada con contraseña",
        ["colSku"] = "SKU", ["colDesc"] = "Descripción", ["colQty"] = "Cantidad", ["colPrice"] = "Precio", ["colAmount"] = "Importe",
        ["perKit"] = "por kit", ["insteadOf"] = "en lugar de", ["exempt"] = "exento", ["backorder"] = "faltante", ["more"] = "… y {0} líneas más.",
        ["subtotal"] = "Subtotal", ["shipping"] = "Envío", ["total"] = "Total", ["authorized"] = "Autorizó", ["sigAttached"] = "firma adjunta",
        ["sigNone"] = "sin firma", ["open"] = "Abrir la orden en Teikem", ["lines1"] = "línea", ["linesN"] = "líneas", ["pickup"] = "Recogido",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["title"] = "Sales order", ["urgent"] = "URGENT", ["orderDate"] = "Order date", ["requestedAt"] = "Requested",
        ["seller"] = "Sales rep", ["captured"] = "Entered by", ["client"] = "Customer", ["phone"] = "Phone", ["merchant"] = "Merchant registration",
        ["newClient"] = "New customer pending approval", ["delivery"] = "Delivery", ["deliverTo"] = "Deliver to", ["pickupAt"] = "Pickup at",
        ["payment"] = "Payment", ["paymentChanged"] = "payment method changed", ["consignment"] = "Consignment", ["required"] = "Required date",
        ["notes"] = "Notes", ["auth"] = "Re-authentication", ["authBiometric"] = "Sent with fingerprint or face", ["authPassword"] = "Sent with password",
        ["colSku"] = "SKU", ["colDesc"] = "Description", ["colQty"] = "Qty", ["colPrice"] = "Price", ["colAmount"] = "Amount",
        ["perKit"] = "per kit", ["insteadOf"] = "instead of", ["exempt"] = "exempt", ["backorder"] = "backorder", ["more"] = "… and {0} more lines.",
        ["subtotal"] = "Subtotal", ["shipping"] = "Shipping", ["total"] = "Total", ["authorized"] = "Authorized by", ["sigAttached"] = "signature attached",
        ["sigNone"] = "no signature", ["open"] = "Open the order in Teikem", ["lines1"] = "line", ["linesN"] = "lines", ["pickup"] = "Pickup",
    };

    private static string Money(SalesOrderEmailModel m, decimal v) => TenantFormatText.Money(v, m.Format);

    private static bool IsDelivery(SalesOrderEmailModel m) => string.Equals(m.DeliveryMode, SalesDeliveryModes.Delivery, StringComparison.OrdinalIgnoreCase);

    /// <summary>Forma de pago con su término («Crédito 30 días»), o null.</summary>
    public static string? PaymentText(SalesOrderEmailModel m)
    {
        if (string.IsNullOrWhiteSpace(m.PaymentMethod)) return string.IsNullOrWhiteSpace(m.PaymentTerm) ? null : m.PaymentTerm!.Trim();
        return string.IsNullOrWhiteSpace(m.PaymentTerm) ? m.PaymentMethod!.Trim() : $"{m.PaymentMethod!.Trim()} {m.PaymentTerm!.Trim()}";
    }

    /// <summary>«Entrega» / «Recogido» (o Delivery / Pickup) según el modo de la orden.</summary>
    public static string DeliveryText(SalesOrderEmailModel m) => L(m, IsDelivery(m) ? "delivery" : "pickup");

    /// <summary>Solo enlaces http/https (el botón nunca lleva otro esquema).</summary>
    public static bool IsSafeLink(string? link)
        => !string.IsNullOrWhiteSpace(link) && Uri.TryCreate(link, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    /// <summary>La línea corta de la campanita y el SMS: «3 líneas · Total $123.45 · Entrega · Crédito 30 días».</summary>
    public static string Summary(SalesOrderEmailModel m)
    {
        var n = m.Lines.Count;
        var parts = new List<string>
        {
            $"{n.ToString(CultureInfo.InvariantCulture)} {L(m, n == 1 ? "lines1" : "linesN")}",
            $"{L(m, "total")} {Money(m, m.Total)}",
            DeliveryText(m),
        };
        if (PaymentText(m) is { } pay) parts.Add(pay);
        return string.Join(" · ", parts);
    }

    /// <summary>El HTML completo del bloque {orderDetail}.</summary>
    public static string Render(SalesOrderEmailModel m)
    {
        var f = m.Format;
        var sb = new StringBuilder(8 * 1024);
        sb.Append("<div style=\"max-width:680px;margin:0 auto;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.45;color:#111827;background:#ffffff\">");

        // encabezado: compañía, número y URGENTE
        sb.Append("<div style=\"background:#f3f4f6;padding:12px 16px;border-radius:6px\">");
        if (!string.IsNullOrWhiteSpace(m.CompanyName)) sb.Append("<div style=\"font-size:12px;").Append(Muted).Append("\">").Append(Encode(m.CompanyName)).Append("</div>");
        sb.Append("<div style=\"font-size:20px;font-weight:bold\">").Append(Encode(L(m, "title"))).Append(' ').Append(Encode(m.OrderNumber));
        if (m.IsUrgent)
            sb.Append(" <span style=\"display:inline-block;background:#b91c1c;color:#ffffff;font-size:12px;padding:2px 8px;border-radius:4px;vertical-align:middle\">")
              .Append(Encode(L(m, "urgent"))).Append("</span>");
        sb.Append("</div></div>");

        // aviso de huella (solo el personal)
        if (m.IsStaff && m.ChangedSinceRequest)
            sb.Append("<div style=\"margin:12px 0;padding:10px 12px;border-left:4px solid #b45309;background:#fffbeb;color:#78350f\">")
              .Append(Encode(m.IsEnglish ? SalesOrderEmailRules.ChangedNoticeEn : SalesOrderEmailRules.ChangedNoticeEs)).Append("</div>");

        // datos generales
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse;margin-top:12px\">");
        void Row(string label, string html)
            => sb.Append("<tr><td valign=\"top\" style=\"").Append(Muted).Append(";white-space:nowrap\">").Append(Encode(label)).Append("</td><td valign=\"top\">").Append(html).Append("</td></tr>");

        Row(L(m, "orderDate"), Encode(TenantFormatText.Date(m.OrderDate, f)));
        if (m.RequestedAtUtc is DateTime at) Row(L(m, "requestedAt"), Encode(TenantFormatText.DateTimeFromUtc(at, f)));
        if (!string.IsNullOrWhiteSpace(m.SellerName) || !string.IsNullOrWhiteSpace(m.CapturedByName))
        {
            var seller = Encode(m.SellerName);
            if (!string.IsNullOrWhiteSpace(m.CapturedByName))
                seller += (seller.Length > 0 ? "<br>" : string.Empty) + "<span style=\"font-size:12px;" + Muted + "\">" + Encode($"{L(m, "captured")}: {m.CapturedByName}") + "</span>";
            Row(L(m, "seller"), seller);
        }

        var client = new StringBuilder();
        client.Append("<strong>").Append(Encode(m.ClientName)).Append("</strong>");
        if (!string.IsNullOrWhiteSpace(m.ClientCode)) client.Append(" (").Append(Encode(m.ClientCode)).Append(')');
        if (m.IsStaff && m.ClientPendingApproval)
            client.Append(" <span style=\"font-size:12px;color:#92400e\">[").Append(Encode(L(m, "newClient"))).Append("]</span>");
        foreach (var line in m.ClientAddress.Where(a => !string.IsNullOrWhiteSpace(a))) client.Append("<br>").Append(Encode(line));
        if (!string.IsNullOrWhiteSpace(m.ClientPhone)) client.Append("<br>").Append(Encode($"{L(m, "phone")} {m.ClientPhone}"));
        if (!string.IsNullOrWhiteSpace(m.MerchantRegistration)) client.Append("<br>").Append(Encode($"{L(m, "merchant")}: {m.MerchantRegistration}"));
        Row(L(m, "client"), client.ToString());

        if (IsDelivery(m))
        {
            var d = new StringBuilder(Encode($"{L(m, "deliverTo")} {m.ConsigneeName}".Trim()));
            foreach (var line in m.ConsigneeAddress.Where(a => !string.IsNullOrWhiteSpace(a))) d.Append("<br>").Append(Encode(line));
            Row(L(m, "delivery"), d.ToString());
        }
        else Row(L(m, "delivery"), Encode($"{L(m, "pickupAt")} {m.WarehouseName}".Trim()));

        var pay = PaymentText(m);
        if (pay is not null || m.IsConsignment)
        {
            var p = new StringBuilder(Encode(pay));
            if (m.IsConsignment) p.Append(p.Length > 0 ? " · " : string.Empty).Append(Encode(L(m, "consignment")));
            if (m.IsStaff && m.PaymentChanged) p.Append(" <span style=\"font-size:12px;color:#92400e\">[").Append(Encode(L(m, "paymentChanged"))).Append("]</span>");
            Row(L(m, "payment"), p.ToString());
        }
        if (m.RequiredDate is DateOnly req) Row(L(m, "required"), Encode(TenantFormatText.Date(req, f)));
        if (m.IsStaff && !string.IsNullOrWhiteSpace(m.Notes)) Row(L(m, "notes"), Encode(m.Notes.Trim()).Replace("\r\n", "\n").Replace("\n", "<br>"));
        if (m.IsStaff && !string.IsNullOrWhiteSpace(m.SubmittedWithAuth))
            Row(L(m, "auth"), Encode(L(m, m.SubmittedWithAuth == SalesSubmitAuth.Biometric ? "authBiometric" : "authPassword")));
        sb.Append("</table>");

        // líneas (con las subfilas de los kits), cortadas en el tope
        sb.Append("<table width=\"100%\" cellpadding=\"5\" cellspacing=\"0\" style=\"border-collapse:collapse;margin-top:16px;font-size:13px\">");
        sb.Append("<tr bgcolor=\"#e5e7eb\"><th align=\"left\">#</th><th align=\"left\">").Append(Encode(L(m, "colSku"))).Append("</th><th align=\"left\">")
          .Append(Encode(L(m, "colDesc"))).Append("</th><th align=\"right\">").Append(Encode(L(m, "colQty"))).Append("</th><th align=\"right\">")
          .Append(Encode(L(m, "colPrice"))).Append("</th><th align=\"right\">").Append(Encode(L(m, "colAmount"))).Append("</th></tr>");
        var (rows, omitted) = SalesOrderEmailRules.Rows(m.Lines);
        var stripe = false;
        foreach (var r in rows)
        {
            if (r.Component is not { } c)
            {
                stripe = !stripe;
                var l = r.Line;
                sb.Append(stripe ? "<tr bgcolor=\"#f9fafb\">" : "<tr>")
                  .Append("<td>").Append(l.LineNo.ToString(CultureInfo.InvariantCulture)).Append("</td><td>").Append(Encode(l.Sku)).Append("</td><td>").Append(Encode(l.Description));
                if (l.IsTaxExempt) sb.Append(" <span style=\"font-size:11px;").Append(Muted).Append("\">(").Append(Encode(L(m, "exempt"))).Append(")</span>");
                if (m.IsStaff && l.Backordered > 0m)
                    sb.Append(" <span style=\"font-size:11px;color:#b91c1c\">[").Append(Encode($"{L(m, "backorder")}: {TenantFormatText.Quantity(l.Backordered, f)}")).Append("]</span>");
                sb.Append("</td><td align=\"right\">").Append(Encode(TenantFormatText.Quantity(l.Quantity, f))).Append("</td><td align=\"right\">").Append(Encode(Money(m, l.UnitPrice)))
                  .Append("</td><td align=\"right\">").Append(Encode(Money(m, l.Amount))).Append("</td></tr>");
            }
            else
            {
                var text = $"↳ {c.Sku} {c.Name} — {TenantFormatText.Quantity(c.QtyPerKit, f)} {L(m, "perKit")} ({TenantFormatText.Quantity(c.TotalQty, f)})";
                if (!string.IsNullOrWhiteSpace(c.SubstitutesSku)) text += $" · {L(m, "insteadOf")} {c.SubstitutesSku}";
                sb.Append(stripe ? "<tr bgcolor=\"#f9fafb\">" : "<tr>").Append("<td></td><td colspan=\"5\" style=\"font-size:12px;").Append(Muted).Append("\">")
                  .Append(Encode(text)).Append("</td></tr>");
            }
        }
        if (omitted > 0)
            sb.Append("<tr><td colspan=\"6\" style=\"").Append(Muted).Append("\">")
              .Append(Encode(string.Format(CultureInfo.InvariantCulture, L(m, "more"), omitted))).Append("</td></tr>");
        sb.Append("</table>");

        // totales: subtotal, envío, un renglón por componente del IVU y total
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse;margin-top:8px\">");
        void Total(string label, string value, bool strong = false)
            => sb.Append("<tr><td align=\"right\"").Append(strong ? " style=\"font-weight:bold\"" : string.Empty).Append('>').Append(Encode(label))
                 .Append("</td><td align=\"right\" style=\"white-space:nowrap").Append(strong ? ";font-weight:bold" : string.Empty).Append("\">").Append(Encode(value)).Append("</td></tr>");
        Total(L(m, "subtotal"), Money(m, m.Subtotal));
        if (m.ShippingCharge != 0m || !string.IsNullOrWhiteSpace(m.ShippingReason))
            Total(string.IsNullOrWhiteSpace(m.ShippingReason) ? L(m, "shipping") : $"{L(m, "shipping")} ({m.ShippingReason!.Trim()})", Money(m, m.ShippingCharge));
        foreach (var t in m.Taxes)
        {
            var label = t.Percent is decimal pct ? $"{t.Label} ({TenantFormatText.Quantity(pct, f)} %)" : t.Label;
            Total(label, t.IsExempt ? L(m, "exempt") : Money(m, t.Amount));
        }
        Total(L(m, "total"), Money(m, m.Total), strong: true);
        sb.Append("</table>");

        // quién autorizó
        if (!string.IsNullOrWhiteSpace(m.AuthorizedByName) || m.HasSignature)
            sb.Append("<p style=\"margin:12px 0 0\">")
              .Append(Encode($"{L(m, "authorized")}: {m.AuthorizedByName?.Trim()} — {L(m, m.HasSignature ? "sigAttached" : "sigNone")}")).Append("</p>");

        // botón (solo el personal y con una dirección web segura)
        if (m.IsStaff && IsSafeLink(m.Link))
            sb.Append("<p style=\"margin:16px 0 4px\"><a href=\"").Append(Encode(m.Link)).Append("\" style=\"display:inline-block;background:#1d4ed8;color:#ffffff;padding:10px 18px;border-radius:6px;text-decoration:none;font-weight:bold\">")
              .Append(Encode(L(m, "open"))).Append("</a></p>");

        sb.Append("</div>");
        return sb.ToString();
    }
}
