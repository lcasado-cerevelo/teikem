using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 2 (P0): vigencia efectivo-fechada (principio #8) y resumen del modelo de facturación. Lógica pura, sin BD.</summary>
public class EffectiveDatedTests
{
    private sealed class Row : IEffectiveDated
    {
        public DateOnly EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
    }

    private static readonly DateOnly Today = new(2026, 9, 25);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);
    private static readonly DateOnly Tomorrow = Today.AddDays(1);

    [Fact]
    public void Open_row_is_current_on_any_date_from_its_start()
    {
        var row = new Row { EffectiveFrom = Yesterday, EffectiveTo = null };
        Assert.True(EffectiveDated.IsCurrentOn(row, Yesterday));
        Assert.True(EffectiveDated.IsCurrentOn(row, Today));
        Assert.True(EffectiveDated.IsCurrentOn(row, Today.AddYears(10)));
        Assert.False(EffectiveDated.IsCurrentOn(row, Yesterday.AddDays(-1)));
    }

    [Fact]
    public void Zero_length_row_closed_today_is_never_current()
    {
        var row = new Row { EffectiveFrom = Today, EffectiveTo = Today };
        Assert.False(EffectiveDated.IsCurrentOn(row, Today));
        Assert.False(EffectiveDated.IsCurrentOn(row, Yesterday));
        Assert.False(EffectiveDated.IsCurrentOn(row, Tomorrow));
    }

    [Fact]
    public void Row_starting_in_the_future_is_not_current_today()
    {
        var row = new Row { EffectiveFrom = Tomorrow, EffectiveTo = null };
        Assert.False(EffectiveDated.IsCurrentOn(row, Today));
        Assert.True(EffectiveDated.IsCurrentOn(row, Tomorrow));
    }

    [Fact]
    public void Row_closed_tomorrow_is_current_today_because_EffectiveTo_is_exclusive()
    {
        var row = new Row { EffectiveFrom = Yesterday, EffectiveTo = Tomorrow };
        Assert.True(EffectiveDated.IsCurrentOn(row, Today));
        Assert.False(EffectiveDated.IsCurrentOn(row, Tomorrow));
    }

    [Fact]
    public void Close_sets_EffectiveTo()
    {
        var row = new Row { EffectiveFrom = Yesterday };
        EffectiveDated.Close(row, Today);
        Assert.Equal(Today, row.EffectiveTo);
        Assert.False(EffectiveDated.IsCurrentOn(row, Today));
        Assert.True(EffectiveDated.IsCurrentOn(row, Yesterday));
    }

    [Fact]
    public void Close_same_day_leaves_a_zero_length_row()
    {
        var row = new Row { EffectiveFrom = Today };
        EffectiveDated.Close(row, Today);
        Assert.Equal(Today, row.EffectiveTo);
        Assert.False(EffectiveDated.IsCurrentOn(row, Today));
    }

    [Fact]
    public void Close_before_EffectiveFrom_throws()
    {
        var row = new Row { EffectiveFrom = Today };
        Assert.Throws<ArgumentException>(() => EffectiveDated.Close(row, Yesterday));
        Assert.Null(row.EffectiveTo);
    }

    [Fact]
    public void ValidateNewVersion_accepts_same_day_and_later_dates()
    {
        var open = new Row { EffectiveFrom = Today };
        EffectiveDated.ValidateNewVersion(open, Today);
        EffectiveDated.ValidateNewVersion(open, Tomorrow);
    }

    [Fact]
    public void ValidateNewVersion_rejects_earlier_date()
    {
        var open = new Row { EffectiveFrom = Today };
        Assert.Throws<ArgumentException>(() => EffectiveDated.ValidateNewVersion(open, Yesterday));
    }
}

public class EffectiveDatedIntervalGuardTests
{
    private sealed class Row : IEffectiveDated
    {
        public DateOnly EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
    }

    private static readonly DateOnly Today = new(2026, 9, 25);

    [Fact]
    public void Open_row_is_live_on_any_date()
        => Assert.True(EffectiveDated.IsLiveOnOrAfter(new Row { EffectiveFrom = Today.AddYears(-1) }, Today));

    [Fact]
    public void Row_closed_with_a_future_date_still_blocks_a_new_row_that_starts_before_that_date()
    {
        // Cierre con fecha futura (2027-01-01): una fila nueva desde hoy convivirá con ella → sigue "viva" desde hoy.
        var row = new Row { EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2027, 1, 1) };
        Assert.True(EffectiveDated.IsLiveOnOrAfter(row, Today));
        Assert.True(EffectiveDated.IsLiveOnOrAfter(row, new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Row_closed_today_or_earlier_does_not_block_a_new_row_from_today()
    {
        var closedToday = new Row { EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = Today };
        var closedEarlier = new Row { EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 3, 1) };
        Assert.False(EffectiveDated.IsLiveOnOrAfter(closedToday, Today));   // EffectiveTo exclusivo
        Assert.False(EffectiveDated.IsLiveOnOrAfter(closedEarlier, Today));
        Assert.True(EffectiveDated.IsLiveOnOrAfter(closedEarlier, new DateOnly(2026, 2, 1))); // alta con vigencia pasada: sí choca
    }

    [Fact]
    public void CloseNotBefore_never_leaves_EffectiveTo_before_EffectiveFrom()
    {
        // Tramo nacido después de la fecha de cierre del componente: queda de longitud cero (CK_RateTier_Dates).
        var bornLater = new Row { EffectiveFrom = Today, EffectiveTo = null };
        EffectiveDated.CloseNotBefore(bornLater, Today.AddDays(-10));
        Assert.Equal(Today, bornLater.EffectiveTo);

        var normal = new Row { EffectiveFrom = Today.AddDays(-30), EffectiveTo = null };
        EffectiveDated.CloseNotBefore(normal, Today);
        Assert.Equal(Today, normal.EffectiveTo);

        var alreadyClosed = new Row { EffectiveFrom = Today.AddDays(-30), EffectiveTo = Today.AddDays(-5) };
        EffectiveDated.CloseNotBefore(alreadyClosed, Today);
        Assert.Equal(Today.AddDays(-5), alreadyClosed.EffectiveTo); // una fila cerrada no se toca
    }
}

public class BillingModelSummaryTests
{
    [Fact]
    public void No_components_selected()
    {
        Assert.Equal("Sin componentes", BillingModelSummary.Build(false, false, false, false, false, "es"));
        Assert.Equal("No components", BillingModelSummary.Build(false, false, false, false, false, "en"));
    }

    [Fact]
    public void All_five_in_fixed_order_es_and_en()
    {
        Assert.Equal("Por servicio, Pieza extra, Despacho, COD, Especiales", BillingModelSummary.Build(true, true, true, true, true, "es"));
        Assert.Equal("Per service, Extra piece, Dispatch, COD, Special", BillingModelSummary.Build(true, true, true, true, true, "en"));
    }

    [Fact]
    public void Partial_combination_keeps_fixed_order()
    {
        Assert.Equal("Por servicio, COD", BillingModelSummary.Build(true, false, false, true, false, "es"));
        Assert.Equal("Pieza extra, Despacho", BillingModelSummary.Build(false, true, true, false, false, "es"));
    }

    [Fact]
    public void Unknown_or_null_language_falls_back_to_spanish()
    {
        Assert.Equal("Por servicio", BillingModelSummary.Build(true, false, false, false, false, null));
        Assert.Equal("Por servicio", BillingModelSummary.Build(true, false, false, false, false, "fr"));
    }
}
