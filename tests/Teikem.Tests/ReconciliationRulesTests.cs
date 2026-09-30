using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 14 (P1) — reglas puras de la conciliación Kárdex ↔ saldo y de la resolución de descuadres (D5): comparación por clave
/// y por producto (la de TraceabilityService), plan de una revisión (abrir, actualizar, cerrarse solo; un descartado con las
/// mismas cifras no se reabre), validación de la solicitud, bloqueos de estatus y chequeo de la reconstrucción, con los
/// mensajes exactos de la sección 3 del plan.
/// </summary>
public class ReconciliationRulesTests
{
    private static readonly BalanceKey K1 = new(1, 10, 100, null);
    private static readonly BalanceKey K2 = new(1, 10, 101, 7);
    private static readonly BalanceKey P2Key = new(2, 10, 100, null);
    private static BalanceKey Total(int p) => new(p, 0, null, null);

    private static IReadOnlyDictionary<BalanceKey, decimal> D(params (BalanceKey K, decimal Q)[] items) => items.ToDictionary(i => i.K, i => i.Q);
    private static IReadOnlyDictionary<int, decimal> P(params (int P, decimal Q)[] items) => items.ToDictionary(i => i.P, i => i.Q);

    // ---------------------------------------------------------------- Compare

    [Fact]
    public void Compare_reports_key_mismatches_including_keys_only_on_one_side()
    {
        var rebuilt = D((K1, 5m), (K2, 3m));
        var balances = D((K1, 5m), (K2, 4m), (P2Key, 2m));
        var rows = ReconciliationRules.Compare(rebuilt, balances, P((1, 8m)), P((1, 9m), (2, 2m)));
        Assert.Equal(2, rows.Count);
        Assert.Equal(new ReconciliationMismatch(K2, 3m, 4m, false), rows[0]);
        Assert.Equal(new ReconciliationMismatch(P2Key, 0m, 2m, false), rows[1]);   // saldo sin movimientos
        Assert.All(rows, r => Assert.Equal(DiscrepancyKinds.Balance, r.KindCode));
    }

    [Fact]
    public void Compare_reports_the_product_total_only_when_every_key_matches()
    {
        // Con una clave descuadrada el total no se repite (sería ruido); con todas las claves cuadradas, el total sí se compara.
        var withKey = ReconciliationRules.Compare(D((K1, 5m)), D((K1, 6m)), P((1, 5m)), P((1, 6m)));
        Assert.DoesNotContain(withKey, r => r.ProductTotal);
        var onlyTotal = ReconciliationRules.Compare(D((K1, 5m)), D((K1, 5m)), P((1, 4m)), P((1, 5m)));
        var row = Assert.Single(onlyTotal);
        Assert.True(row.ProductTotal);
        Assert.Equal(Total(1), row.Key);
        Assert.Equal(DiscrepancyKinds.ProductTotal, row.KindCode);
        Assert.Equal(1m, ReconciliationRules.Difference(row.LedgerQty, row.BalanceQty));
        Assert.Empty(ReconciliationRules.Compare(D((K1, 5m)), D((K1, 5m)), P((1, 5m)), P((1, 5m))));
    }

    [Fact]
    public void Compare_orders_by_product_key_before_total_warehouse_bin_and_lot()
    {
        var rebuilt = D((new BalanceKey(2, 10, 5, null), 1m), (new BalanceKey(1, 20, 1, null), 1m), (new BalanceKey(1, 10, 9, null), 1m));
        var rows = ReconciliationRules.Compare(rebuilt, D(), P(), P((3, 1m)));
        Assert.Equal(new[] { (1, 10), (1, 20), (2, 10), (3, 0) }, rows.Select(r => (r.Key.ProductId, r.Key.WarehouseId)).ToArray());
    }

    // ---------------------------------------------------------------- Plan

    private static StoredDiscrepancy Open(int id, BalanceKey k, decimal l, decimal b, string kind = DiscrepancyKinds.Balance) => new(id, kind, k, l, b);

    [Fact]
    public void Plan_opens_new_updates_existing_and_closes_what_balances_again()
    {
        var mismatches = new[] { new ReconciliationMismatch(K1, 5m, 6m, false), new ReconciliationMismatch(K2, 3m, 1m, false) };
        var plan = ReconciliationRules.Plan(new[] { Open(1, K1, 5m, 7m), Open(2, P2Key, 1m, 2m) }, Array.Empty<StoredDiscrepancy>(), mismatches,
            new HashSet<int> { 1, 2 });
        Assert.Equal(K2, Assert.Single(plan.ToOpen).Key);
        var (id, m) = Assert.Single(plan.ToUpdate);
        Assert.Equal(1, id);
        Assert.Equal(6m, m.BalanceQty);
        Assert.Equal(new[] { 2 }, plan.ToSelfCorrect);
    }

    [Fact]
    public void Plan_does_not_touch_open_discrepancies_of_products_not_checked()
    {
        var plan = ReconciliationRules.Plan(new[] { Open(9, P2Key, 1m, 2m) }, Array.Empty<StoredDiscrepancy>(), Array.Empty<ReconciliationMismatch>(),
            new HashSet<int> { 1 });
        Assert.Empty(plan.ToSelfCorrect);
        // NULL = todo el tenant revisado: se cierra solo.
        Assert.Equal(new[] { 9 }, ReconciliationRules.Plan(new[] { Open(9, P2Key, 1m, 2m) }, Array.Empty<StoredDiscrepancy>(),
            Array.Empty<ReconciliationMismatch>(), null).ToSelfCorrect);
    }

    [Fact]
    public void Plan_does_not_reopen_a_dismissed_one_with_the_same_numbers_but_does_with_others()
    {
        var dismissed = new[] { Open(5, K1, 5m, 6m) };
        var same = ReconciliationRules.Plan(Array.Empty<StoredDiscrepancy>(), dismissed, new[] { new ReconciliationMismatch(K1, 5m, 6m, false) }, null);
        Assert.Empty(same.ToOpen);
        var other = ReconciliationRules.Plan(Array.Empty<StoredDiscrepancy>(), dismissed, new[] { new ReconciliationMismatch(K1, 5m, 7m, false) }, null);
        Assert.Single(other.ToOpen);
    }

    [Fact]
    public void Plan_leaves_an_open_product_total_alone_while_keys_of_the_product_are_off()
    {
        var openTotal = Open(3, Total(1), 5m, 6m, DiscrepancyKinds.ProductTotal);
        var withKey = ReconciliationRules.Plan(new[] { openTotal }, Array.Empty<StoredDiscrepancy>(),
            new[] { new ReconciliationMismatch(K1, 5m, 6m, false) }, new HashSet<int> { 1 });
        Assert.Empty(withKey.ToSelfCorrect);
        Assert.Empty(withKey.ToUpdate);
        var clean = ReconciliationRules.Plan(new[] { openTotal }, Array.Empty<StoredDiscrepancy>(), Array.Empty<ReconciliationMismatch>(),
            new HashSet<int> { 1 });
        Assert.Equal(new[] { 3 }, clean.ToSelfCorrect);
    }

    // ---------------------------------------------------------------- resolución

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("FIX")]
    public void Resolve_request_requires_a_known_action(string? action)
    {
        var (code, _, errors) = ReconciliationRules.ValidateResolveRequest(action, "nota");
        Assert.Null(code);
        Assert.Equal("Indique la acción: REBUILD_BALANCE (corregir el saldo) o DISMISS (descartar).", Assert.Single(errors["action"]));
    }

    [Fact]
    public void Resolve_request_notes_rules()
    {
        var (code, notes, errors) = ReconciliationRules.ValidateResolveRequest(" dismiss ", "  ");
        Assert.Equal(ReconciliationRules.ActionDismiss, code);
        Assert.Null(notes);
        Assert.Equal("Escriba una nota que explique por qué se descarta el descuadre.", Assert.Single(errors["notes"]));

        var rebuild = ReconciliationRules.ValidateResolveRequest("rebuild_balance", null);
        Assert.Equal(ReconciliationRules.ActionRebuild, rebuild.Action);
        Assert.Empty(rebuild.Errors);

        var tooLong = ReconciliationRules.ValidateResolveRequest("REBUILD_BALANCE", new string('x', 501));
        Assert.Equal("La nota admite como máximo 500 caracteres.", Assert.Single(tooLong.Errors["notes"]));
        Assert.Empty(ReconciliationRules.ValidateResolveRequest("DISMISS", new string('x', 500)).Errors);
    }

    [Fact]
    public void Resolve_blockers_are_closed_and_product_total_rebuild()
    {
        Assert.Equal("El descuadre ya está cerrado; solo se consulta.",
            ReconciliationRules.ResolveBlocker(true, DiscrepancyKinds.Balance, ReconciliationRules.ActionDismiss));
        Assert.Equal("Este descuadre es del total del producto; no se corrige por posición. Corrija los descuadres por posición o descártelo con una nota.",
            ReconciliationRules.ResolveBlocker(false, DiscrepancyKinds.ProductTotal, ReconciliationRules.ActionRebuild));
        Assert.Null(ReconciliationRules.ResolveBlocker(false, DiscrepancyKinds.ProductTotal, ReconciliationRules.ActionDismiss));
        Assert.Null(ReconciliationRules.ResolveBlocker(false, DiscrepancyKinds.Balance, ReconciliationRules.ActionRebuild));
    }

    [Fact]
    public void Rebuild_check_rejects_negative_and_below_reserved()
    {
        Assert.Equal("El Kárdex da un saldo negativo (-2) para SKU-1 en A-01; revise los movimientos antes de corregir el saldo.",
            ReconciliationRules.RebuildCheck(-2m, 0m, "SKU-1", "A-01"));
        Assert.Equal("El Kárdex da 3 para SKU-1 en A-01, menos que lo reservado (4.5); libere la reserva antes de corregir el saldo.",
            ReconciliationRules.RebuildCheck(3m, 4.5m, "SKU-1", "A-01"));
        Assert.Null(ReconciliationRules.RebuildCheck(4.5m, 4.5m, "SKU-1", "A-01"));
        Assert.Null(ReconciliationRules.RebuildCheck(0m, 0m, "SKU-1", "A-01"));
    }

    [Fact]
    public void Manual_run_limit_and_messages()
    {
        Assert.Equal(200, ReconciliationRules.MaxManualProducts);
        Assert.Equal("La conciliación manual admite como máximo 200 productos a la vez.", ReconciliationRules.ManualTooManyProducts);
        Assert.Equal("Kárdex 2, saldo 5.5.", ReconciliationRules.OpenedComment(2m, 5.5m));
    }
}
