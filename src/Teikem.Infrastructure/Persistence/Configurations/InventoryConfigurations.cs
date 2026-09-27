using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Wms;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 6 — Inventario (CAPA 8): lotes, series, saldos y ledger. QtyAvailable es columna computada PERSISTED en SQL: se mapea
// como computada y NUNCA se usa en lógica (InMemory no la calcula; el disponible sale de InventoryRules.Available).
// UQ_StockBalance se mapea con HasFilter(null) (EF agregaría 'IS NOT NULL' por las columnas anulables). InventoryTransaction es
// de solo inserción (su Id es BIGINT). Ni StockBalance ni InventoryTransaction se auditan: el rastro es el propio ledger.

public sealed class InventoryLotConfiguration : IEntityTypeConfiguration<InventoryLot>
{
    public void Configure(EntityTypeBuilder<InventoryLot> b)
    {
        b.ToTable("InventoryLot");
        b.HasKey(l => l.LotId);
        b.Property(l => l.LotNumber).HasMaxLength(60).IsRequired();
        b.Property(l => l.ManufactureDate).HasColumnType("date");
        b.Property(l => l.ExpiryDate).HasColumnType("date");

        b.HasIndex(l => new { l.ProductId, l.LotNumber }).IsUnique().HasDatabaseName("UQ_Lot");

        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class InventorySerialConfiguration : IEntityTypeConfiguration<InventorySerial>
{
    public void Configure(EntityTypeBuilder<InventorySerial> b)
    {
        b.ToTable("InventorySerial");
        b.HasKey(s => s.SerialId);
        b.Property(s => s.SerialNumber).HasMaxLength(80).IsRequired();

        b.HasIndex(s => new { s.ProductId, s.SerialNumber }).IsUnique().HasDatabaseName("UQ_Serial");
        b.HasIndex(s => s.CurrentBinId).HasFilter("[CurrentBinId] IS NOT NULL").HasDatabaseName("IX_Serial_CurrentBin");

        b.HasOne<Product>().WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryLot>().WithMany().HasForeignKey(s => s.LotId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Teikem.Domain.Catalogs.StatusCode>().WithMany().HasForeignKey(s => s.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> b)
    {
        b.ToTable("StockBalance");
        b.HasKey(s => s.StockBalanceId);
        b.Property(s => s.QtyOnHand).HasColumnType("decimal(16,3)");
        b.Property(s => s.QtyReserved).HasColumnType("decimal(16,3)");
        b.Property(s => s.QtyAvailable).HasColumnType("decimal(16,3)").HasComputedColumnSql("[QtyOnHand]-[QtyReserved]", stored: true);
        b.Property(s => s.RowVersion).IsRowVersion();

        b.HasIndex(s => new { s.ProductId, s.WarehouseId, s.WarehouseBinId, s.LotId }).IsUnique().HasFilter(null).HasDatabaseName("UQ_StockBalance");
        b.HasIndex(s => new { s.WarehouseId, s.ProductId }).HasDatabaseName("IX_StockBalance_WH");
        b.HasIndex(s => s.WarehouseBinId).HasDatabaseName("IX_StockBalance_Bin");

        b.HasOne<Product>().WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(s => s.WarehouseBinId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryLot>().WithMany().HasForeignKey(s => s.LotId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> b)
    {
        b.ToTable("InventoryTransaction");
        b.HasKey(t => t.InventoryTransactionId);
        b.Property(t => t.Quantity).HasColumnType("decimal(16,3)");
        b.Property(t => t.Notes).HasMaxLength(300);

        b.HasIndex(t => new { t.ProductId, t.CreatedAtUtc }).HasDatabaseName("IX_InvTxn_Product");
        b.HasIndex(t => new { t.RefEntityLookupId, t.RefId }).HasDatabaseName("IX_InvTxn_Ref");
        b.HasIndex(t => new { t.TenantId, t.CreatedAtUtc }).HasDatabaseName("IX_InvTxn_Tenant_Date");

        b.HasOne<Product>().WithMany().HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryLot>().WithMany().HasForeignKey(t => t.LotId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventorySerial>().WithMany().HasForeignKey(t => t.SerialId).OnDelete(DeleteBehavior.NoAction);
    }
}
