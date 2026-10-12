using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Wms;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Ajuste múltiple AJM-###### (2026-10-12; docs/ajuste-multiple-diseno.md). Mapeo 1:1 con la sección del 2026-10-12 de Diseño/logistica-db-update.sql.

public sealed class MultiAdjustmentConfiguration : IEntityTypeConfiguration<MultiAdjustment>
{
    public void Configure(EntityTypeBuilder<MultiAdjustment> b)
    {
        b.ToTable("MultiAdjustment");
        b.HasKey(a => a.MultiAdjustmentId);
        b.Property(a => a.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(a => a.Number).HasMaxLength(40).IsRequired();
        b.Property(a => a.Notes).HasMaxLength(300).IsRequired();
        b.Property(a => a.UnitsUp).HasColumnType("decimal(16,3)");
        b.Property(a => a.UnitsDown).HasColumnType("decimal(16,3)");

        b.HasIndex(a => a.PublicId).IsUnique().HasDatabaseName("UX_MultiAdjustment_PublicId");
        b.HasIndex(a => new { a.TenantId, a.Number }).IsUnique().HasDatabaseName("UQ_MultiAdjustment_Number");
        b.HasIndex(a => new { a.TenantId, a.CreatedAtUtc }).HasDatabaseName("IX_MultiAdjustment_Tenant_Date");

        b.HasOne<Warehouse>().WithMany().HasForeignKey(a => a.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(a => a.Lines).WithOne().HasForeignKey(l => l.MultiAdjustmentId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class MultiAdjustmentLineConfiguration : IEntityTypeConfiguration<MultiAdjustmentLine>
{
    public void Configure(EntityTypeBuilder<MultiAdjustmentLine> b)
    {
        b.ToTable("MultiAdjustmentLine");
        b.HasKey(l => l.MultiAdjustmentLineId);
        b.Property(l => l.SystemQty).HasColumnType("decimal(16,3)");
        b.Property(l => l.CountedQty).HasColumnType("decimal(16,3)");
        b.Property(l => l.Difference).HasColumnType("decimal(17,3)");

        b.HasIndex(l => l.MultiAdjustmentId).HasDatabaseName("IX_MultiAdjustmentLine_Doc");

        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(l => l.WarehouseBinId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryLot>().WithMany().HasForeignKey(l => l.LotId).OnDelete(DeleteBehavior.NoAction);
    }
}
