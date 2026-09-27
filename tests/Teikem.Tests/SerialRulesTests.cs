using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 6 / P0 — normalización de series y estatus destino por movimiento (D16, D34).</summary>
public class SerialRulesTests
{
    [Fact]
    public void Normalize_trims_and_rejects_empty_long_duplicates_and_too_many()
    {
        Assert.Equal(new[] { "S1", "S2" }, SerialRules.Normalize(new[] { " S1 ", "S2" }).Serials);
        Assert.Equal(SerialRules.Empty, SerialRules.Normalize(new[] { "S1", " " }).Error);
        Assert.Equal(SerialRules.TooLong, SerialRules.Normalize(new[] { new string('X', 81) }).Error);
        Assert.Equal(SerialRules.Duplicated("s1"), SerialRules.Normalize(new[] { "S1", "s1" }).Error);
        Assert.Equal(SerialRules.TooMany, SerialRules.Normalize(Enumerable.Range(1, 501).Select(i => "S" + i)).Error);
        Assert.Empty(SerialRules.Normalize(null).Serials);
    }

    [Fact]
    public void Target_status_by_type_and_sign()
    {
        Assert.Equal(SerialStatuses.Available, SerialRules.TargetStatus(InventoryTxnTypes.Receipt, 1));
        Assert.Equal(SerialStatuses.Available, SerialRules.TargetStatus(InventoryTxnTypes.Adjustment, 1));
        Assert.Equal(SerialStatuses.Shipped, SerialRules.TargetStatus(InventoryTxnTypes.Issue, -1));
        Assert.Equal(SerialStatuses.Shipped, SerialRules.TargetStatus(InventoryTxnTypes.CrossDock, -1));
        Assert.Equal(SerialStatuses.Scrapped, SerialRules.TargetStatus(InventoryTxnTypes.Adjustment, -1));
        Assert.Null(SerialRules.TargetStatus(InventoryTxnTypes.Transfer, 1));
    }

    [Fact]
    public void Exact_messages()
    {
        Assert.Equal("La serie S1 no está disponible en A01.", SerialRules.NotAvailable("S1", "A01"));
        Assert.Equal("La serie S1 ya está en inventario.", SerialRules.AlreadyInStock("S1"));
        Assert.Equal("La serie S1 fue dada de baja; no vuelve al inventario.", SerialRules.Scrapped("S1"));
    }
}
