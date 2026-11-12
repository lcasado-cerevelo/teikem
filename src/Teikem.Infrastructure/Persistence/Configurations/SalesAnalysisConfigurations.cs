using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Sales;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 241 — análisis de ventas. Mapeo 1:1 con las tablas de la sección «2026-11-12 — Lote 241» de logistica-db-update.sql.

public sealed class SalesRouteConfiguration : IEntityTypeConfiguration<SalesRoute>
{
    public void Configure(EntityTypeBuilder<SalesRoute> b)
    {
        b.ToTable("SalesRoute");
        b.HasKey(x => x.SalesRouteId);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Name }).HasDatabaseName("UX_SalesRoute_Name").IsUnique().HasFilter("[IsActive] = 1");
    }
}

public sealed class SalesRepConfiguration : IEntityTypeConfiguration<SalesRep>
{
    public void Configure(EntityTypeBuilder<SalesRep> b)
    {
        b.ToTable("SalesRep");
        b.HasKey(x => x.SalesRepId);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Name }).HasDatabaseName("UX_SalesRep_Name").IsUnique().HasFilter("[IsActive] = 1");
        b.HasOne<SalesRoute>().WithMany().HasForeignKey(x => x.SalesRouteId);
        // dbo.Client.SalesRepId (columna del lote 241) → el representante del cliente
        b.HasMany<Teikem.Domain.Clients.Client>().WithOne().HasForeignKey(c => c.SalesRepId);
    }
}

public sealed class SalesQuotaConfiguration : IEntityTypeConfiguration<SalesQuota>
{
    public void Configure(EntityTypeBuilder<SalesQuota> b)
    {
        b.ToTable("SalesQuota");
        b.HasKey(x => x.SalesQuotaId);
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.HasIndex(x => new { x.TenantId, x.SalesRepId, x.Month }).HasDatabaseName("UX_SalesQuota_RepMonth").IsUnique();
        b.HasOne<SalesRep>().WithMany().HasForeignKey(x => x.SalesRepId);
    }
}

public sealed class ProductLineConfiguration : IEntityTypeConfiguration<ProductLine>
{
    public void Configure(EntityTypeBuilder<ProductLine> b)
    {
        b.ToTable("ProductLine");
        b.HasKey(x => x.ProductLineId);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Name }).HasDatabaseName("UX_ProductLine_Name").IsUnique().HasFilter("[IsActive] = 1");
    }
}

public sealed class ProductLineBrandConfiguration : IEntityTypeConfiguration<ProductLineBrand>
{
    public void Configure(EntityTypeBuilder<ProductLineBrand> b)
    {
        b.ToTable("ProductLineBrand");
        b.HasKey(x => x.ProductLineBrandId);
        b.Property(x => x.Brand).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Brand }).HasDatabaseName("UX_ProductLineBrand_Brand").IsUnique();
        b.HasOne<ProductLine>().WithMany().HasForeignKey(x => x.ProductLineId);
    }
}

public sealed class SalesAnalysisSettingConfiguration : IEntityTypeConfiguration<SalesAnalysisSetting>
{
    public void Configure(EntityTypeBuilder<SalesAnalysisSetting> b)
    {
        b.ToTable("SalesAnalysisSetting");
        b.HasKey(x => x.TenantId);
        b.Property(x => x.TenantId).ValueGeneratedNever();
        b.Property(x => x.ServiceLevelZ).HasColumnType("decimal(6,3)");
    }
}

public sealed class SalesHistoryBatchConfiguration : IEntityTypeConfiguration<SalesHistoryBatch>
{
    public void Configure(EntityTypeBuilder<SalesHistoryBatch> b)
    {
        b.ToTable("SalesHistoryBatch");
        b.HasKey(x => x.SalesHistoryBatchId);
        b.Property(x => x.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(x => x.Kind).HasColumnType("varchar(10)").IsRequired();
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.FileHash).HasColumnType("char(64)").IsRequired();
        b.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.Source).HasColumnType("varchar(10)").IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Kind, x.FileHash }).HasDatabaseName("IX_SalesHistoryBatch_Hash");
    }
}

public sealed class SalesHistoryLineConfiguration : IEntityTypeConfiguration<SalesHistoryLine>
{
    public void Configure(EntityTypeBuilder<SalesHistoryLine> b)
    {
        b.ToTable("SalesHistoryLine");
        b.HasKey(x => x.SalesHistoryLineId);
        b.Property(x => x.TxnType).HasMaxLength(30).IsRequired();
        b.Property(x => x.DocNumber).HasMaxLength(40);
        b.Property(x => x.Memo).HasMaxLength(250);
        b.Property(x => x.ClientNameRaw).HasMaxLength(200).IsRequired();
        b.Property(x => x.Section).HasMaxLength(60);
        b.Property(x => x.ItemRaw).HasMaxLength(300).IsRequired();
        b.Property(x => x.ItemSku).HasMaxLength(100).IsRequired();
        b.Property(x => x.Quantity).HasColumnType("decimal(18,4)");
        b.Property(x => x.UnitPrice).HasColumnType("decimal(18,5)");
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.LineClass).HasColumnType("varchar(10)").IsRequired();
        b.HasIndex(x => new { x.TenantId, x.TxnDate }).HasDatabaseName("IX_SalesHistoryLine_Date");
        b.HasIndex(x => new { x.TenantId, x.ProductId, x.TxnDate }).HasDatabaseName("IX_SalesHistoryLine_Product");
        b.HasIndex(x => new { x.TenantId, x.ClientId, x.TxnDate }).HasDatabaseName("IX_SalesHistoryLine_Client");
        b.HasIndex(x => x.SalesHistoryBatchId).HasDatabaseName("IX_SalesHistoryLine_Batch");
        b.HasOne<SalesHistoryBatch>().WithMany().HasForeignKey(x => x.SalesHistoryBatchId);
    }
}

public sealed class PurchaseHistoryLineConfiguration : IEntityTypeConfiguration<PurchaseHistoryLine>
{
    public void Configure(EntityTypeBuilder<PurchaseHistoryLine> b)
    {
        b.ToTable("PurchaseHistoryLine");
        b.HasKey(x => x.PurchaseHistoryLineId);
        b.Property(x => x.TxnType).HasMaxLength(30).IsRequired();
        b.Property(x => x.DocNumber).HasMaxLength(40);
        b.Property(x => x.SupplierNameRaw).HasMaxLength(200).IsRequired();
        b.Property(x => x.ItemRaw).HasMaxLength(300).IsRequired();
        b.Property(x => x.ItemSku).HasMaxLength(100).IsRequired();
        b.Property(x => x.Quantity).HasColumnType("decimal(18,4)");
        b.Property(x => x.UnitCost).HasColumnType("decimal(18,5)");
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.HasIndex(x => new { x.TenantId, x.TxnDate }).HasDatabaseName("IX_PurchaseHistoryLine_Date");
        b.HasIndex(x => new { x.TenantId, x.ProductId, x.TxnDate }).HasDatabaseName("IX_PurchaseHistoryLine_Product");
        b.HasIndex(x => x.SalesHistoryBatchId).HasDatabaseName("IX_PurchaseHistoryLine_Batch");
        b.HasOne<SalesHistoryBatch>().WithMany().HasForeignKey(x => x.SalesHistoryBatchId);
    }
}

public sealed class SalesHistoryAliasConfiguration : IEntityTypeConfiguration<SalesHistoryAlias>
{
    public void Configure(EntityTypeBuilder<SalesHistoryAlias> b)
    {
        b.ToTable("SalesHistoryAlias");
        b.HasKey(x => x.SalesHistoryAliasId);
        b.Property(x => x.Kind).HasColumnType("varchar(10)").IsRequired();
        b.Property(x => x.RawKey).HasMaxLength(300).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Kind, x.RawKey }).HasDatabaseName("UX_SalesHistoryAlias_Key").IsUnique();
    }
}
