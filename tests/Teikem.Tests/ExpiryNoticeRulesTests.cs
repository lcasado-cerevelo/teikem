using Teikem.Domain.Clients;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 248 — umbrales de los avisos de vencimiento (validar, leer, guardar), días de aviso, ventana hacia atrás, decisión del aviso y clave de no repetición.</summary>
public sealed class ExpiryNoticeRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 27);
    private static readonly int[] Default = { 30, 15, 5 };

    [Fact]
    public void Mensajes_y_constantes()
    {
        Assert.Equal("Cada umbral es un número entero de días entre -365 y 365 (positivo = antes de vencer, 0 = el día, negativo = después).", ExpiryNoticeRules.ThresholdOutOfRange);
        Assert.Equal("Hay un umbral repetido.", ExpiryNoticeRules.ThresholdRepeated);
        Assert.Equal("Se pueden poner hasta 10 umbrales.", ExpiryNoticeRules.TooManyThresholds);
        Assert.Equal("30,15,5", DocumentExpiryCodes.DefaultThresholds);
        Assert.Equal(Default, ExpiryNoticeRules.DefaultThresholds);
        Assert.Equal((500, 7, "CD", "SP"), (DocumentExpiryCodes.MaxNoticesPerRun, DocumentExpiryCodes.RecoveryDays, DocumentExpiryCodes.ClientDocumentPrefix, DocumentExpiryCodes.SpecialPricePrefix));
    }

    [Fact]
    public void Validate_normaliza_de_mayor_a_menor_y_da_el_error_exacto()
    {
        Assert.Equal(new[] { 30, 5, 0, -7 }, ExpiryNoticeRules.Validate(new[] { 5, -7, 30, 0 }).Thresholds);
        Assert.Empty(ExpiryNoticeRules.Validate(null).Thresholds);
        Assert.Null(ExpiryNoticeRules.Validate(Array.Empty<int>()).Error);
        Assert.Null(ExpiryNoticeRules.Validate(new[] { 365, -365 }).Error);
        Assert.Equal(ExpiryNoticeRules.ThresholdOutOfRange, ExpiryNoticeRules.Validate(new[] { 366 }).Error);
        Assert.Equal(ExpiryNoticeRules.ThresholdOutOfRange, ExpiryNoticeRules.Validate(new[] { -366 }).Error);
        Assert.Equal(ExpiryNoticeRules.ThresholdRepeated, ExpiryNoticeRules.Validate(new[] { 5, 15, 5 }).Error);
        Assert.Equal(ExpiryNoticeRules.TooManyThresholds, ExpiryNoticeRules.Validate(Enumerable.Range(1, 11).ToArray()).Error);
        Assert.Null(ExpiryNoticeRules.Validate(Enumerable.Range(1, 10).ToArray()).Error);
    }

    [Fact]
    public void ParseThresholds_y_Format()
    {
        Assert.Equal(Default, ExpiryNoticeRules.ParseThresholds("30,15,5").Thresholds);
        Assert.Equal(new[] { 15, 0, -3 }, ExpiryNoticeRules.ParseThresholds(" -3 , 15,,0 ").Thresholds);
        Assert.Empty(ExpiryNoticeRules.ParseThresholds("").Thresholds);
        Assert.Equal(ExpiryNoticeRules.ThresholdOutOfRange, ExpiryNoticeRules.ParseThresholds("30,abc").Error);
        Assert.Equal(ExpiryNoticeRules.ThresholdRepeated, ExpiryNoticeRules.ParseThresholds("5,5").Error);
        Assert.Equal(Default, ExpiryNoticeRules.ThresholdsOrDefault("x"));
        Assert.Equal(new[] { 10 }, ExpiryNoticeRules.ThresholdsOrDefault("10"));
        Assert.Equal("30,15,0,-7", ExpiryNoticeRules.Format(new[] { 0, 15, -7, 30 }));
        Assert.True(ExpiryNoticeRules.Format(Enumerable.Range(-365, 10)).Length <= 60);
    }

    [Fact]
    public void WarnDays_y_LookbackDays()
    {
        Assert.Equal(30, ExpiryNoticeRules.WarnDays(Default));
        Assert.Equal(0, ExpiryNoticeRules.WarnDays(new[] { 0, -5 }));
        Assert.Equal(0, ExpiryNoticeRules.WarnDays(Array.Empty<int>()));
        Assert.Null(ExpiryNoticeRules.LookbackDays(Default, dailyAfterExpiry: true));
        Assert.Equal(0, ExpiryNoticeRules.LookbackDays(Default, false));
        Assert.Equal(7, ExpiryNoticeRules.LookbackDays(new[] { 15, 0 }, false));
        Assert.Equal(17, ExpiryNoticeRules.LookbackDays(new[] { 15, 0, -10 }, false));
    }

    [Theory]
    // días que faltan, umbrales, diario → (vencido, umbral, marca) o null
    [InlineData(31, "30,15,5", false, null, null, null)]
    [InlineData(30, "30,15,5", false, false, 30, "T30")]
    [InlineData(20, "30,15,5", false, false, 30, "T30")]
    [InlineData(15, "30,15,5", false, false, 15, "T15")]
    [InlineData(1, "30,15,5", false, false, 5, "T5")]      // capturado tarde: solo el umbral más cercano
    [InlineData(0, "30,15,5", false, false, 5, "T5")]
    [InlineData(0, "30,15,0", false, false, 0, "T0")]
    [InlineData(-1, "30,15,5", false, null, null, null)]   // sin umbrales ≤ 0 no se avisa lo vencido
    [InlineData(-1, "30,0", false, true, 0, "T0")]          // recupera el aviso del día 0
    [InlineData(-7, "30,0", false, true, 0, "T0")]
    [InlineData(-8, "30,0", false, null, null, null)]      // fuera de la ventana de recuperación
    [InlineData(-10, "0,-7", false, true, -7, "T-7")]
    [InlineData(-14, "0,-7", false, true, -7, "T-7")]
    [InlineData(-15, "0,-7", false, null, null, null)]
    [InlineData(-400, "30,15,5", true, true, null, "D20261127")]   // diario: cada día, sin límite
    [InlineData(10, "30,15,5", true, false, 15, "T15")]
    public void Decide_por_tabla(int days, string thresholds, bool daily, bool? expired, int? threshold, string? marker)
    {
        var d = ExpiryNoticeRules.Decide(Today.AddDays(days), Today, ExpiryNoticeRules.ParseThresholds(thresholds).Thresholds, daily);
        if (expired is null) { Assert.Null(d); return; }
        Assert.Equal(new ExpiryNoticeDecision(expired.Value, threshold, marker!), d);
    }

    [Fact]
    public void Decide_sin_vencidos_cuando_la_fuente_no_los_avisa()
    {
        Assert.Null(ExpiryNoticeRules.Decide(Today.AddDays(-1), Today, new[] { 30, 0 }, false, allowExpired: false));
        Assert.Null(ExpiryNoticeRules.Decide(Today.AddDays(-1), Today, new[] { 30, 0 }, true, allowExpired: false));
        Assert.Equal("T0", ExpiryNoticeRules.Decide(Today, Today, new[] { 30, 0 }, true, allowExpired: false)!.Marker);
    }

    [Fact]
    public void DedupKey_con_fecha_y_marca()
    {
        Assert.Equal("CD:123:20261231:T15", ExpiryNoticeRules.DedupKey("CD", 123, new DateOnly(2026, 12, 31), "T15"));
        Assert.Equal("SP:9:20260105:T-7", ExpiryNoticeRules.DedupKey("SP", 9, new DateOnly(2026, 1, 5), "T-7"));
        Assert.Throws<ArgumentException>(() => ExpiryNoticeRules.DedupKey(new string('X', 90), int.MaxValue, Today, "T0"));
    }
}
