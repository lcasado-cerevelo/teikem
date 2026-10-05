using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Wms;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 6 — Almacenes (CAPA 7) y productos (CAPA 8). Mapeo 1:1 con Diseño/logistica-db-estructura.sql. Los índices únicos
// (filtrados o no) y los CHECKs viven en SQL como última línea; aquí se espejan con el mismo nombre y filtro. EF agrega
// 'IS NOT NULL' a los índices únicos con columnas anulables: UQ_Product_Sku se mapea con HasFilter(null). Las FKs compuestas
// (Id, TenantId), (Posición, Almacén) y (Lote/Serie, Producto) NO se mapean: EF sigue las relaciones por columna simple, sin
// cascadas (NoAction), y nunca genera esquema. Warehouse.GeoPoint (GEOGRAPHY) no se mapea.

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.ToTable("Warehouse");
        b.HasKey(w => w.WarehouseId);
        b.Property(w => w.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(w => w.Code).HasMaxLength(30).IsRequired();
        b.Property(w => w.Name).HasMaxLength(150).IsRequired();
        b.Property(w => w.Line1).HasMaxLength(200);
        b.Property(w => w.City).HasMaxLength(100);
        b.Property(w => w.State).HasMaxLength(100);
        b.Property(w => w.PostalCode).HasMaxLength(20);
        b.Property(w => w.RowVersion).IsRowVersion();

        b.HasIndex(w => new { w.TenantId, w.Code }).IsUnique().HasDatabaseName("UQ_Warehouse_Code");

        // Lote 16: ReceivingModeLookupId (FK_Warehouse_ReceivingMode → LookupCode) y DefaultReceivingBinId
        // (FK_Warehouse_DefaultReceivingBin, compuesta con WarehouseId) viven en SQL; en EF quedan como columnas simples, sin
        // relación: una relación Warehouse → WarehouseBin cerraría un ciclo con WarehouseBin → Warehouse al insertar ambos.
        b.Property(w => w.ReceivingModeLookupId);
        b.Property(w => w.DefaultReceivingBinId);

        b.HasOne(w => w.Status).WithMany().HasForeignKey(w => w.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(w => w.Zones).WithOne(z => z.Warehouse).HasForeignKey(z => z.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(w => w.Docks).WithOne().HasForeignKey(d => d.WarehouseId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class WarehouseZoneConfiguration : IEntityTypeConfiguration<WarehouseZone>
{
    public void Configure(EntityTypeBuilder<WarehouseZone> b)
    {
        b.ToTable("WarehouseZone");
        b.HasKey(z => z.WarehouseZoneId);
        b.Property(z => z.Code).HasMaxLength(30).IsRequired();
        b.Property(z => z.Name).HasMaxLength(120).IsRequired();

        b.HasIndex(z => new { z.WarehouseId, z.Code }).IsUnique().HasDatabaseName("UQ_WarehouseZone");

        b.HasMany(z => z.Bins).WithOne(x => x.Zone).HasForeignKey(x => x.WarehouseZoneId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class WarehouseBinConfiguration : IEntityTypeConfiguration<WarehouseBin>
{
    public void Configure(EntityTypeBuilder<WarehouseBin> b)
    {
        b.ToTable("WarehouseBin");
        b.HasKey(x => x.WarehouseBinId);
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Aisle).HasMaxLength(20);
        b.Property(x => x.Rack).HasMaxLength(20);
        b.Property(x => x.Level).HasMaxLength(20);
        b.Property(x => x.Position).HasMaxLength(20);
        b.Property(x => x.MaxWeightKg).HasColumnType("decimal(12,3)");
        b.Property(x => x.MaxCapacityQty).HasColumnType("int");

        b.HasIndex(x => new { x.WarehouseZoneId, x.Code }).IsUnique().HasDatabaseName("UQ_WarehouseBin");
        // Código de posición único por almacén (D18).
        b.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique().HasDatabaseName("UQ_WarehouseBin_WhCode");
        // Lote 21: posiciones provisionales pendientes de revisión.
        b.Property(x => x.IsProvisional).HasDefaultValue(false);
        b.HasIndex(x => new { x.WarehouseId, x.IsProvisional }).HasFilter("[IsProvisional] = 1").HasDatabaseName("IX_WarehouseBin_Provisional");

        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class WarehouseDockConfiguration : IEntityTypeConfiguration<WarehouseDock>
{
    public void Configure(EntityTypeBuilder<WarehouseDock> b)
    {
        b.ToTable("WarehouseDock");
        b.HasKey(d => d.WarehouseDockId);
        b.Property(d => d.Code).HasMaxLength(30).IsRequired();

        b.HasIndex(d => new { d.WarehouseId, d.Code }).IsUnique().HasDatabaseName("UQ_WarehouseDock");

        b.HasOne(d => d.Status).WithMany().HasForeignKey(d => d.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> b)
    {
        b.ToTable("ProductCategory");
        b.HasKey(c => c.ProductCategoryId);
        b.Property(c => c.Name).HasMaxLength(150).IsRequired();

        // Nombre único entre activas por nivel (el padre NULL = raíz; SQL Server trata NULL como un valor en índices únicos).
        b.HasIndex(c => new { c.TenantId, c.ParentId, c.Name }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_ProductCategory_Name");

        b.HasOne<ProductCategory>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Product");
        b.HasKey(p => p.ProductId);
        b.Property(p => p.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(p => p.Sku).HasMaxLength(60).IsRequired();
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.WeightKg).HasColumnType("decimal(12,3)");
        b.Property(p => p.VolumeM3).HasColumnType("decimal(12,4)");
        b.Property(p => p.Barcode).HasMaxLength(60);
        b.Property(p => p.PurchaseCost).HasColumnType("decimal(18,4)");
        b.Property(p => p.SalePrice).HasColumnType("decimal(18,4)");
        b.Property(p => p.MinQty).HasColumnType("decimal(16,3)");
        b.Property(p => p.MinPickQty).HasColumnType("decimal(16,3)");
        b.Property(p => p.MaxPickQty).HasColumnType("decimal(16,3)");
        b.Property(p => p.Brand).HasMaxLength(100);   // Lote 12
        b.Property(p => p.Model).HasMaxLength(100);   // Lote 12
        b.Property(p => p.RowVersion).IsRowVersion();

        // SKU único por dueño (ClientId NULL = propio): sin el filtro 'IS NOT NULL' que EF agregaría.
        b.HasIndex(p => new { p.TenantId, p.ClientId, p.Sku }).IsUnique().HasFilter(null).HasDatabaseName("UQ_Product_Sku");
        b.HasIndex(p => new { p.TenantId, p.Barcode }).IsUnique().HasFilter("[Barcode] IS NOT NULL AND [IsActive] = 1").HasDatabaseName("UX_Product_Barcode");

        b.HasOne<Teikem.Domain.Clients.Client>().WithMany().HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ProductCategory>().WithMany().HasForeignKey(p => p.ProductCategoryId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(p => p.PreferredWarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(p => p.PreferredBinId).OnDelete(DeleteBehavior.NoAction);
    }
}
