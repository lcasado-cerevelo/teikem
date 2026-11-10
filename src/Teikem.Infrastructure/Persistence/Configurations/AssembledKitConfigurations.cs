using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Wms;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 232 — kit armado: armado ARM-##### (con sus líneas) y desarmado DES-##### (con sus líneas). Mapeo 1:1 con la sección «2026-11-10 — Lote 232» de
// Diseño/logistica-db-update.sql.

public sealed class KitAssemblyConfiguration : IEntityTypeConfiguration<KitAssembly>
{
    public void Configure(EntityTypeBuilder<KitAssembly> b)
    {
        b.ToTable("KitAssembly");
        b.HasKey(a => a.KitAssemblyId);
        b.Property(a => a.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(a => a.Number).HasMaxLength(40).IsRequired();
        b.Property(a => a.Notes).HasMaxLength(300);
        b.Property(a => a.Quantity).HasColumnType("decimal(16,3)");
        b.Property(a => a.DisassembledQty).HasColumnType("decimal(16,3)");
        b.Property(a => a.UnitCost).HasColumnType("decimal(18,4)");
        b.Property(a => a.TotalCost).HasColumnType("decimal(18,4)");

        b.HasIndex(a => a.PublicId).IsUnique().HasDatabaseName("UX_KitAssembly_PublicId");
        b.HasIndex(a => new { a.TenantId, a.Number }).IsUnique().HasDatabaseName("UQ_KitAssembly_Number");
        b.HasIndex(a => new { a.TenantId, a.KitProductId, a.CreatedAtUtc }).HasDatabaseName("IX_KitAssembly_Kit");

        b.HasOne<Product>().WithMany().HasForeignKey(a => a.KitProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(a => a.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(a => a.ToBinId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryLot>().WithMany().HasForeignKey(a => a.KitLotId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(a => a.Lines).WithOne().HasForeignKey(l => l.KitAssemblyId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class KitAssemblyLineConfiguration : IEntityTypeConfiguration<KitAssemblyLine>
{
    public void Configure(EntityTypeBuilder<KitAssemblyLine> b)
    {
        b.ToTable("KitAssemblyLine");
        b.HasKey(l => l.KitAssemblyLineId);
        b.Property(l => l.QtyPerKit).HasColumnType("decimal(16,3)");
        b.Property(l => l.Quantity).HasColumnType("decimal(16,3)");
        b.Property(l => l.ReturnedQty).HasColumnType("decimal(16,3)");
        b.Property(l => l.UnitCost).HasColumnType("decimal(18,4)");

        b.HasIndex(l => l.KitAssemblyId).HasDatabaseName("IX_KitAssemblyLine_Doc");
        b.HasIndex(l => new { l.TenantId, l.ComponentProductId }).HasDatabaseName("IX_KitAssemblyLine_Component");

        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ComponentProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(l => l.FromBinId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryLot>().WithMany().HasForeignKey(l => l.LotId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class KitDisassemblyConfiguration : IEntityTypeConfiguration<KitDisassembly>
{
    public void Configure(EntityTypeBuilder<KitDisassembly> b)
    {
        b.ToTable("KitDisassembly");
        b.HasKey(d => d.KitDisassemblyId);
        b.Property(d => d.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(d => d.Number).HasMaxLength(40).IsRequired();
        b.Property(d => d.Notes).HasMaxLength(300);
        b.Property(d => d.Quantity).HasColumnType("decimal(16,3)");

        b.HasIndex(d => d.PublicId).IsUnique().HasDatabaseName("UX_KitDisassembly_PublicId");
        b.HasIndex(d => new { d.TenantId, d.Number }).IsUnique().HasDatabaseName("UQ_KitDisassembly_Number");
        b.HasIndex(d => d.KitAssemblyId).HasDatabaseName("IX_KitDisassembly_Assembly");

        b.HasOne<KitAssembly>().WithMany().HasForeignKey(d => d.KitAssemblyId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Product>().WithMany().HasForeignKey(d => d.KitProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(d => d.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryLot>().WithMany().HasForeignKey(d => d.KitLotId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(d => d.Lines).WithOne().HasForeignKey(l => l.KitDisassemblyId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class KitDisassemblyLineConfiguration : IEntityTypeConfiguration<KitDisassemblyLine>
{
    public void Configure(EntityTypeBuilder<KitDisassemblyLine> b)
    {
        b.ToTable("KitDisassemblyLine");
        b.HasKey(l => l.KitDisassemblyLineId);
        b.Property(l => l.Quantity).HasColumnType("decimal(16,3)");
        b.Property(l => l.UnitCost).HasColumnType("decimal(18,4)");

        b.HasIndex(l => l.KitDisassemblyId).HasDatabaseName("IX_KitDisassemblyLine_Doc");
        b.HasIndex(l => new { l.TenantId, l.ComponentProductId }).HasDatabaseName("IX_KitDisassemblyLine_Component");

        b.HasOne<KitAssemblyLine>().WithMany().HasForeignKey(l => l.KitAssemblyLineId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ComponentProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(l => l.ToBinId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryLot>().WithMany().HasForeignKey(l => l.LotId).OnDelete(DeleteBehavior.NoAction);
    }
}
