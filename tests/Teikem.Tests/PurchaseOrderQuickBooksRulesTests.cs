using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Xunit;
using R = Teikem.Domain.Wms.PurchaseOrderQuickBooksRules;
using S = Teikem.Domain.Sales.QuickBooksSyncStates;

namespace Teikem.Tests;

/// <summary>
/// Lote 246 (Lote F del plan de Solutions, P1) — reglas puras de las órdenes de compra frente a QuickBooks: el estado genérico (QuickBooksSync) con su prioridad fija
/// FROM_QUICKBOOKS &gt; IN_QUICKBOOKS &gt; EXPORTED &gt; PENDING &gt; NOT_EXPORTABLE, qué impide exportar, confirmar, marcar, «No pasó» y quitar la marca (mensaje y código),
/// el memo del archivo, la lectura de los filtros, el origen y los textos de los avisos.
/// </summary>
public sealed class PurchaseOrderQuickBooksRulesTests
{
    private static readonly DateTime Exported = new(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Marked = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------- estado

    public static TheoryData<string?, string, bool, bool, string> Priority()
    {
        var data = new TheoryData<string?, string, bool, bool, string>();
        var statuses = new[] { PurchaseOrderStatuses.Draft, PurchaseOrderStatuses.Sent, PurchaseOrderStatuses.Partial, PurchaseOrderStatuses.Received, PurchaseOrderStatuses.Cancelled };
        foreach (var origin in new[] { PurchaseOrderOrigins.Teikem, PurchaseOrderOrigins.Excel, PurchaseOrderOrigins.QuickBooks, null })
            foreach (var status in statuses)
                foreach (var exported in new[] { false, true })
                    foreach (var marked in new[] { false, true })
                    {
                        var eligible = status is PurchaseOrderStatuses.Sent or PurchaseOrderStatuses.Partial or PurchaseOrderStatuses.Received;
                        var expected = origin == PurchaseOrderOrigins.QuickBooks ? S.FromQuickBooks
                            : marked ? S.InQuickBooks
                            : exported ? S.Exported
                            : eligible ? S.Pending
                            : S.NotExportable;
                        data.Add(origin, status, exported, marked, expected);
                    }
        return data;
    }

    [Theory]
    [MemberData(nameof(Priority))]
    public void The_state_follows_the_fixed_priority(string? origin, string status, bool exported, bool marked, string expected)
        => Assert.Equal(expected, R.State(origin, status, exported ? Exported : null, marked ? Marked : null));

    [Fact]
    public void Exported_and_then_cancelled_stays_exported_and_the_generic_function_is_the_same()
    {
        Assert.Equal(S.Exported, R.State(PurchaseOrderOrigins.Teikem, PurchaseOrderStatuses.Cancelled, Exported, null));
        Assert.Equal(S.NotExportable, R.State(null, PurchaseOrderStatuses.Cancelled, null, null));
        Assert.Equal(S.Pending, R.State(null, PurchaseOrderStatuses.Sent, null, null));   // NULL = TEIKEM
        Assert.Equal(S.FromQuickBooks, QuickBooksSync.State(true, true, Exported, Marked));
        Assert.Equal(S.InQuickBooks, QuickBooksSync.State(false, false, Exported, Marked));
        Assert.Equal(S.Exported, QuickBooksSync.State(false, false, Exported, null));
        Assert.Equal(S.Pending, QuickBooksSync.State(false, true, null, null));
        Assert.Equal(S.NotExportable, QuickBooksSync.State(false, false, null, null));
        Assert.Equal(new[] { "PENDING", "EXPORTED", "IN_QUICKBOOKS", "FROM_QUICKBOOKS", "NOT_EXPORTABLE" }, S.All);
        Assert.Equal(new[] { "SENT", "PARTIAL", "RECEIVED" }.OrderBy(x => x), R.ExportableStatuses.OrderBy(x => x));
        Assert.Equal(1000, R.MaxIdsPerCall);
    }

    // ---------------------------------------------------------------- qué impide cada acción

    [Fact]
    public void Export_errors_with_their_exact_message_and_code()
    {
        Assert.Equal((422, "La orden de compra PO-00001 vino de QuickBooks: no se exporta ni se marca."),
            R.ExportError("PO-00001", PurchaseOrderOrigins.QuickBooks, PurchaseOrderStatuses.Sent, "Enviada", null));
        Assert.Equal((409, "La orden de compra PO-00002 ya está en QuickBooks; para volver a exportarla, quítele la marca."),
            R.ExportError("PO-00002", PurchaseOrderOrigins.Teikem, PurchaseOrderStatuses.Sent, "Enviada", Marked));
        Assert.Equal((422, "La orden de compra PO-00003 está en Borrador: solo se pasan a QuickBooks las enviadas, recibidas en parte o recibidas."),
            R.ExportError("PO-00003", null, PurchaseOrderStatuses.Draft, "Borrador", null));
        Assert.Equal(422, R.ExportError("PO-00004", PurchaseOrderOrigins.Excel, PurchaseOrderStatuses.Cancelled, "Cancelada", null)!.Value.Status);
        foreach (var status in R.ExportableStatuses)
        {
            Assert.Null(R.ExportError("PO-00005", PurchaseOrderOrigins.Teikem, status, status, null));
            Assert.Null(R.ExportError("PO-00005", PurchaseOrderOrigins.Excel, status, status, null));
        }
    }

    [Fact]
    public void Confirm_and_mark_errors_with_their_exact_message_and_code()
    {
        // «Sí, ya están»: solo las exportadas, en cualquier estatus
        Assert.Equal((422, "La orden de compra PO-00001 no ha salido en ningún archivo de QuickBooks; para marcarla a mano use «Marcar como en QuickBooks»."),
            R.MarkError("PO-00001", PurchaseOrderOrigins.Teikem, PurchaseOrderStatuses.Sent, "Enviada", null, null, confirm: true));
        Assert.Null(R.MarkError("PO-00001", PurchaseOrderOrigins.Teikem, PurchaseOrderStatuses.Cancelled, "Cancelada", Exported, null, confirm: true));
        // a mano: elegibles o ya exportadas
        Assert.Null(R.MarkError("PO-00002", PurchaseOrderOrigins.Excel, PurchaseOrderStatuses.Received, "Recibida", null, null, confirm: false));
        Assert.Equal((422, "La orden de compra PO-00002 está en Borrador: solo se pasan a QuickBooks las enviadas, recibidas en parte o recibidas."),
            R.MarkError("PO-00002", PurchaseOrderOrigins.Excel, PurchaseOrderStatuses.Draft, "Borrador", null, null, confirm: false));
        Assert.Null(R.MarkError("PO-00002", PurchaseOrderOrigins.Excel, PurchaseOrderStatuses.Cancelled, "Cancelada", Exported, null, confirm: false));
        // ya marcada: sin cambio, no es error; de QuickBooks: 422
        Assert.Null(R.MarkError("PO-00003", PurchaseOrderOrigins.Teikem, PurchaseOrderStatuses.Sent, "Enviada", null, Marked, confirm: true));
        Assert.Equal((422, R.FromQuickBooks("PO-00004")), R.MarkError("PO-00004", PurchaseOrderOrigins.QuickBooks, PurchaseOrderStatuses.Sent, "Enviada", null, null, confirm: false));
        Assert.Equal((422, R.FromQuickBooks("PO-00004")), R.UnmarkError("PO-00004", PurchaseOrderOrigins.QuickBooks));
        Assert.Null(R.UnmarkError("PO-00005", null));
    }

    [Fact]
    public void Not_passed_errors_with_their_exact_message_and_code()
    {
        Assert.Equal((409, "La orden de compra PO-00001 ya está marcada como en QuickBooks: quítele la marca en vez de usar «No pasó»."),
            R.NotPassedError("PO-00001", PurchaseOrderOrigins.Teikem, Marked));
        Assert.Equal((422, R.FromQuickBooks("PO-00002")), R.NotPassedError("PO-00002", PurchaseOrderOrigins.QuickBooks, null));
        Assert.Null(R.NotPassedError("PO-00003", PurchaseOrderOrigins.Excel, null));   // exportada o nunca exportada: no es error (la nunca exportada queda igual)
    }

    [Fact]
    public void The_fixed_messages()
    {
        Assert.Equal("No hay órdenes de compra por pasar a QuickBooks.", R.NothingPending);
        Assert.Equal("Indique las órdenes de compra (purchaseOrderIds) o pida todas (all = true).", R.SelectionRequired);
        Assert.Equal("No se pueden marcar más de 1000 órdenes de compra en una sola llamada; use «todas».", R.TooManyIds);
        Assert.Equal("Alguna de las órdenes de compra indicadas no existe.", R.NotFound);
    }

    // ---------------------------------------------------------------- memo, filtros y origen

    [Fact]
    public void The_memo_drops_the_import_mark_tabs_and_line_breaks_and_is_cut_at_4000()
    {
        Assert.Equal("Importada de QuickBooks 1001.", R.Memo("Importada de QuickBooks 1001. [QB:ACME|1001]"));
        Assert.Equal("Importada de archivo X-1. Llamar antes", R.Memo("Importada de archivo X-1. [IMP:ACME|X-1]\r\nLlamar\tantes"));
        Assert.Null(R.Memo("[QB:ACME|1]"));
        Assert.Null(R.Memo("   "));
        Assert.Null(R.Memo(null));
        Assert.Equal(4000, R.Memo(new string('a', 4100))!.Length);
        Assert.Equal("[otro] texto", R.Memo("[otro]\n\ntexto"));   // solo quita las marcas de importación
    }

    [Fact]
    public void Parse_states_reads_repeated_or_comma_separated_values_and_rejects_unknown_ones()
    {
        var (states, error) = R.ParseStates(new[] { "pending", "EXPORTED,in_quickbooks", null, " ", "PENDING" });
        Assert.Null(error);
        Assert.Equal(new[] { S.Pending, S.Exported, S.InQuickBooks }, states);
        Assert.Empty(R.ParseStates(null).States);

        var bad = R.ParseStates(new[] { "PENDING", "LISTA" });
        Assert.Empty(bad.States);
        Assert.Equal("El estado de QuickBooks «LISTA» no es válido: use PENDING, EXPORTED, IN_QUICKBOOKS, FROM_QUICKBOOKS o NOT_EXPORTABLE.", bad.Error);
    }

    [Fact]
    public void Origins_normalize_default_to_teikem_and_reject_unknown_ones()
    {
        Assert.Equal("PurchaseOrderOrigin", PurchaseOrderOrigins.Domain);
        Assert.Equal(new[] { "TEIKEM", "EXCEL", "QUICKBOOKS" }, PurchaseOrderOrigins.All);
        Assert.Equal(PurchaseOrderOrigins.Excel, PurchaseOrderOrigins.Normalize(" excel "));
        Assert.Equal(string.Empty, PurchaseOrderOrigins.Normalize("GENERIC"));
        Assert.Equal(string.Empty, PurchaseOrderOrigins.Normalize(null));
        Assert.Equal(PurchaseOrderOrigins.Teikem, PurchaseOrderOrigins.OrDefault(null));
        Assert.Equal(PurchaseOrderOrigins.QuickBooks, PurchaseOrderOrigins.OrDefault("quickbooks"));
        // GENERIC y TEMPLATE cuentan como EXCEL (se ofrecen para exportar); QUICKBOOKS como QUICKBOOKS
        Assert.Equal(PurchaseOrderOrigins.Excel, PurchaseOrderOrigins.FromImportSource("GENERIC"));
        Assert.Equal(PurchaseOrderOrigins.Excel, PurchaseOrderOrigins.FromImportSource("TEMPLATE"));
        Assert.Equal(PurchaseOrderOrigins.QuickBooks, PurchaseOrderOrigins.FromImportSource("quickbooks"));
        Assert.Equal("El origen «WEB» no es válido: use TEIKEM, EXCEL o QUICKBOOKS.", PurchaseOrderOrigins.UnknownOrigin("WEB"));
    }

    // ---------------------------------------------------------------- avisos

    [Fact]
    public void The_warning_texts()
    {
        Assert.Equal("El término «NET30» no tiene nombre en QuickBooks (Ajustes → Operación → QuickBooks): el proveedor nuevo sale sin término.",
            R.UnmappedSupplierTerm("NET30"));
        Assert.Equal("La orden de compra PO-00001 suma 1.0001 en Teikem y 1.00 en el archivo: QuickBooks redondea cada línea a centavos.",
            R.RoundingDifference("PO-00001", 1.0001m, 1.00m));
        Assert.Equal("La orden de compra PO-00001 suma 1,234.50 en Teikem y 1,234.48 en el archivo: QuickBooks redondea cada línea a centavos.",
            R.RoundingDifference("PO-00001", 1234.5m, 1234.48m));
        Assert.Equal("La orden de compra PO-00002 está en EUR y la compañía en USD: el archivo no lleva moneda y QuickBooks la toma en la de la compañía.",
            R.CurrencyDiffers("PO-00002", "EUR", "USD"));
        Assert.Equal("El proveedor «Café €» tiene caracteres que QuickBooks no admite (€); en el archivo salen como «?».",
            R.UnencodableText("El proveedor «Café €»", "€"));
    }
}
