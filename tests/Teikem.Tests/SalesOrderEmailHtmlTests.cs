using System.Text;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Notifications;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 243 — el HTML del correo con la orden de venta (como Formsite), la línea de resumen y la huella (lógica pura).</summary>
public sealed class SalesOrderEmailHtmlTests
{
    private static SalesOrderEmailModel Model(Func<SalesOrderEmailModel, SalesOrderEmailModel>? change = null)
    {
        var m = new SalesOrderEmailModel
        {
            Lang = "es", CompanyName = "Solutions PR", OrderNumber = "OV-00012", IsUrgent = true,
            OrderDate = new DateOnly(2026, 11, 24), RequestedAtUtc = new DateTime(2026, 11, 24, 18, 5, 0, DateTimeKind.Utc),
            SellerName = "Ana Pérez", CapturedByName = "Luis Rivera",
            ClientName = "Farmacia <b>Central</b>", ClientCode = "FARM-01", ClientAddress = new[] { "Calle Sol 10", "Ponce PR 00716" },
            ClientPhone = "(787) 555-1234", MerchantRegistration = "SURI-12345", ClientPendingApproval = true,
            DeliveryMode = SalesDeliveryModes.Delivery, ConsigneeName = "Sucursal Ponce", ConsigneeAddress = new[] { "Calle Comercio 25", "Ponce PR 00730" },
            WarehouseName = "Almacén principal", PaymentMethod = "Crédito", PaymentTerm = "30 días", PaymentChanged = true, IsConsignment = false,
            RequiredDate = new DateOnly(2026, 11, 26), Notes = "Nota <b>prueba</b>", SubmittedWithAuth = SalesSubmitAuth.Password,
            Lines = new[]
            {
                new SalesOrderEmailLine(1, "MED-500", "Acetaminofén 500 mg", 3m, 12.50m, 37.50m, Backordered: 1m),
                new SalesOrderEmailLine(2, "VIT-C", "Vitamina C", 2m, 8.75m, 17.50m, IsTaxExempt: true),
                new SalesOrderEmailLine(3, "KIT-BOT", "Botiquín básico", 2m, 25m, 50m, Components: new[]
                {
                    new SalesOrderEmailComponent("VEN-01", "Vendas elásticas", 2m, 4m),
                    new SalesOrderEmailComponent("ALC-70", "Alcohol 70 %", 1m, 2m, "ALC-60"),
                }),
            },
            Subtotal = 105m, ShippingCharge = 7.50m, ShippingReason = "Regla de la compañía",
            Taxes = new[] { new SalesOrderEmailTax("IVU estatal", 10.5m, 9.19m), new SalesOrderEmailTax("IVU municipal", 1m, 0.88m) },
            Total = 122.57m, AuthorizedByName = "María López", HasSignature = true,
            Link = "https://app.teikem.com/sales-orders?order=abc", ChangedSinceRequest = false,
        };
        return change is null ? m : change(m);
    }

    [Fact]
    public void El_correo_del_personal_lleva_todo_lo_de_la_orden()
    {
        var html = SalesOrderEmailHtml.Render(Model());
        foreach (var expected in new[]
                 {
                     "Solutions PR", "Orden de venta OV-00012", "URGENTE", "Fecha de la orden", "11/24/2026", "Solicitada", "11/24/2026 2:05 PM",
                     "Ana Pérez", "Capturó: Luis Rivera", "(FARM-01)", "Calle Sol 10", "Ponce PR 00716", "Tel. (787) 555-1234", "Registro de comerciante: SURI-12345",
                     "Cliente nuevo por aprobar", "Entrega a Sucursal Ponce", "Calle Comercio 25", "Crédito 30 días", "forma de pago cambiada", "Fecha requerida", "11/26/2026",
                     "Notas", "Enviada con contraseña",
                     ">MED-500<", "Acetaminofén 500 mg", ">3<", "$12.50", "$37.50", "faltante: 1", "(exento)",
                     "↳ VEN-01 Vendas elásticas — 2 por kit (4)", "↳ ALC-70 Alcohol 70 % — 1 por kit (2) · en lugar de ALC-60",
                     "Subtotal", "$105.00", "Envío (Regla de la compañía)", "$7.50", "IVU estatal (10.5 %)", "$9.19", "IVU municipal (1 %)", "$0.88", "Total", "$122.57",
                     "Autorizó: María López — firma adjunta", "Abrir la orden en Teikem", "href=\"https://app.teikem.com/sales-orders?order=abc\"",
                 })
            Assert.Contains(expected, html);
        Assert.Contains("max-width:680px", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("<style", html);
        Assert.DoesNotContain("Esta orden cambió", html);
    }

    [Fact]
    public void Todo_valor_sale_codificado()
    {
        var html = SalesOrderEmailHtml.Render(Model());
        Assert.Contains("Nota &lt;b&gt;prueba&lt;/b&gt;", html);
        Assert.Contains("Farmacia &lt;b&gt;Central&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>prueba", html);
        Assert.DoesNotContain("<b>Central", html);
        Assert.Equal("&lt;a href=&quot;x&quot;&gt;&amp;&#39;", SalesOrderEmailHtml.Encode("<a href=\"x\">&'"));
    }

    [Fact]
    public void Las_etiquetas_salen_en_ingles()
    {
        var html = SalesOrderEmailHtml.Render(Model(m => m with { Lang = "en" }));
        foreach (var expected in new[] { "Sales order OV-00012", "URGENT", "Order date", "Sales rep", "Entered by: Luis Rivera", "Customer", "Deliver to Sucursal Ponce",
                     "Payment", "Required date", "Description", "Qty", "per kit", "instead of ALC-60", "Shipping (Regla de la compañía)", "Authorized by: María López — signature attached",
                     "Open the order in Teikem", "(exempt)" })
            Assert.Contains(expected, html);
        Assert.DoesNotContain("Fecha de la orden", html);
    }

    [Fact]
    public void La_copia_al_cliente_no_lleva_enlace_notas_ni_marcas_internas()
    {
        var html = SalesOrderEmailHtml.Render(Model(m => m with { Audience = NotificationAudience.Customer, ChangedSinceRequest = true }));
        foreach (var hidden in new[] { "Abrir la orden", "href=", "prueba", "Notas", "forma de pago cambiada", "Cliente nuevo por aprobar", "faltante", "Enviada con contraseña", "Esta orden cambió" })
            Assert.DoesNotContain(hidden, html);
        // lo demás sí
        foreach (var shown in new[] { "OV-00012", "MED-500", "Crédito 30 días", "IVU estatal", "$122.57", "Autorizó: María López — firma adjunta" })
            Assert.Contains(shown, html);
    }

    [Fact]
    public void Con_la_huella_distinta_el_personal_ve_el_aviso_de_cambio()
    {
        var html = SalesOrderEmailHtml.Render(Model(m => m with { ChangedSinceRequest = true }));
        Assert.Contains("Esta orden cambió después de solicitarse; el correo muestra los datos vigentes.", html);
        Assert.Contains("This order changed after it was requested", SalesOrderEmailHtml.Render(Model(m => m with { ChangedSinceRequest = true, Lang = "en" })));
    }

    [Fact]
    public void Recogido_sin_firma_sin_enlace_y_sin_urgente()
    {
        var html = SalesOrderEmailHtml.Render(Model(m => m with
        {
            DeliveryMode = SalesDeliveryModes.Pickup, HasSignature = false, Link = null, IsUrgent = false, CapturedByName = null,
        }));
        Assert.Contains("Recogido en Almacén principal", html);
        Assert.Contains("Autorizó: María López — sin firma", html);
        Assert.DoesNotContain("Abrir la orden", html);
        Assert.DoesNotContain("URGENTE", html);
        Assert.DoesNotContain("Capturó", html);
        Assert.DoesNotContain("Entrega a", html);
    }

    [Fact]
    public void Un_enlace_que_no_es_http_no_pone_boton()
    {
        Assert.DoesNotContain("Abrir la orden", SalesOrderEmailHtml.Render(Model(m => m with { Link = "javascript:alert(1)" })));
        Assert.DoesNotContain("Abrir la orden", SalesOrderEmailHtml.Render(Model(m => m with { Link = "/sales-orders?order=abc" })));
    }

    [Fact]
    public void Un_componente_exento_del_IVU_dice_exento()
    {
        var html = SalesOrderEmailHtml.Render(Model(m => m with { Taxes = new[] { new SalesOrderEmailTax("IVU estatal", 10.5m, 9.19m), new SalesOrderEmailTax("IVU municipal", 1m, 0m, IsExempt: true) } }));
        Assert.Contains("IVU municipal (1 %)</td><td align=\"right\" style=\"white-space:nowrap\">exento", html);
    }

    private static SalesOrderEmailLine[] ManyLines(int count, int nameLength = 20)
        => Enumerable.Range(1, count).Select(i => new SalesOrderEmailLine(i, $"SKU-{i:00000}-{new string('X', 20)}", new string('N', nameLength) + $" {i}", 12m, 1234.56m, 14814.72m)).ToArray();

    [Fact]
    public void A_partir_de_200_filas_se_corta_y_dice_cuantas_faltan()
    {
        var (rows, omitted) = SalesOrderEmailRules.Rows(ManyLines(250));
        Assert.Equal((200, 50), (rows.Count, omitted));
        var html = SalesOrderEmailHtml.Render(Model(m => m with { Lines = ManyLines(250) }));
        Assert.Contains("… y 50 líneas más.", html);
        Assert.Contains("SKU-00200-", html);
        Assert.DoesNotContain("SKU-00201-", html);
        Assert.Contains("… and 50 more lines.", SalesOrderEmailHtml.Render(Model(m => m with { Lines = ManyLines(250), Lang = "en" })));

        // exactamente 200 no se corta; las subfilas de los kits cuentan
        Assert.Equal(0, SalesOrderEmailRules.Rows(ManyLines(200)).Omitted);
        var kit = new SalesOrderEmailLine(1, "KIT", "Kit", 1m, 1m, 1m, Components: Enumerable.Range(1, 205).Select(i => new SalesOrderEmailComponent($"C{i}", "Pieza", 1m, 1m)).ToArray());
        var cut = SalesOrderEmailRules.Rows(new[] { kit });
        Assert.Equal((200, 6), (cut.Rows.Count, cut.Omitted));
    }

    [Fact]
    public void El_peor_caso_pesa_menos_de_100_KB()
    {
        // 200 filas (y más) con nombres largos, dentro del texto del correo y con todos los datos
        var model = Model(m => m with { Lines = ManyLines(260, nameLength: 150), Notes = new string('n', 1000) });
        var html = "<p>Se solicitó la orden OV-00012 de Farmacia Central para Almacén principal.</p>" + SalesOrderEmailHtml.Render(model);
        Assert.True(Encoding.UTF8.GetByteCount(html) < SalesOrderEmailRules.MaxHtmlBytes, $"El correo pesa {Encoding.UTF8.GetByteCount(html)} bytes.");
    }

    [Fact]
    public void El_resumen_es_una_linea_corta()
    {
        Assert.Equal("3 líneas · Total $122.57 · Entrega · Crédito 30 días", SalesOrderEmailHtml.Summary(Model()));
        Assert.Equal("3 lines · Total $122.57 · Delivery · Crédito 30 días", SalesOrderEmailHtml.Summary(Model(m => m with { Lang = "en" })));
        Assert.Equal("1 línea · Total $5.00 · Recogido",
            SalesOrderEmailHtml.Summary(Model(m => m with { Lines = ManyLines(1), Total = 5m, DeliveryMode = SalesDeliveryModes.Pickup, PaymentMethod = null, PaymentTerm = null })));
    }

    [Fact]
    public void La_huella_es_estable_y_cambia_con_cantidad_precio_o_total()
    {
        var lines = new[] { (1, 10, 4m, 10m), (2, 11, 1m, 2.5m) };
        var fp = SalesOrderEmailRules.Fingerprint(lines, 42.5m);
        Assert.Equal(16, fp.Length);
        Assert.Matches("^[0-9a-f]{16}$", fp);
        Assert.Equal(fp, SalesOrderEmailRules.Fingerprint(new[] { (2, 11, 1.000m, 2.50m), (1, 10, 4.0000m, 10.00m) }, 42.50m));   // orden y escala no importan
        Assert.NotEqual(fp, SalesOrderEmailRules.Fingerprint(new[] { (1, 10, 5m, 10m), (2, 11, 1m, 2.5m) }, 42.5m));
        Assert.NotEqual(fp, SalesOrderEmailRules.Fingerprint(new[] { (1, 10, 4m, 11m), (2, 11, 1m, 2.5m) }, 42.5m));
        Assert.NotEqual(fp, SalesOrderEmailRules.Fingerprint(lines, 43m));

        Assert.True(SalesOrderEmailRules.Changed("0000000000000000", fp));
        Assert.False(SalesOrderEmailRules.Changed(fp.ToUpperInvariant(), fp));
        Assert.False(SalesOrderEmailRules.Changed(null, fp));   // aviso viejo sin huella: no se avisa
        Assert.False(SalesOrderEmailRules.Changed("  ", fp));
    }
}
