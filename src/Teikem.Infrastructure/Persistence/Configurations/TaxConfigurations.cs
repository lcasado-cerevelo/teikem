using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Tax;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 55 (P3) — impuestos (IVU). Mapeo 1:1 con la sección «2026-10-15 (j)» de Diseño/logistica-db-update.sql.
// Los CHECK viven en SQL; aquí se espejan los índices con el mismo nombre.

public sealed class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> b)
    {
        b.ToTable("TaxRate");
        b.HasKey(r => r.TaxRateId);
        b.Property(r => r.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(r => r.Code).HasMaxLength(30).IsUnicode(false).IsRequired();
        b.Property(r => r.Name).HasMaxLength(100).IsRequired();
        b.Property(r => r.Percent).HasColumnType("decimal(9,4)");
        b.Property(r => r.EffectiveFrom).HasColumnType("date");
        b.Property(r => r.EffectiveTo).HasColumnType("date");
        b.HasMany(r => r.Components).WithOne().HasForeignKey(c => c.TaxRateId);
        b.HasIndex(r => r.PublicId).IsUnique().HasDatabaseName("UQ_TaxRate_PublicId");
        b.HasIndex(r => new { r.TenantId, r.Code, r.EffectiveFrom }).IsUnique().HasDatabaseName("UX_TaxRate_Code_From");
    }
}

public sealed class TaxRateComponentConfiguration : IEntityTypeConfiguration<TaxRateComponent>
{
    public void Configure(EntityTypeBuilder<TaxRateComponent> b)
    {
        b.ToTable("TaxRateComponent");
        b.HasKey(c => c.TaxRateComponentId);
        b.Property(c => c.Code).HasMaxLength(30).IsUnicode(false).IsRequired();
        b.Property(c => c.Name).HasMaxLength(100).IsRequired();
        b.Property(c => c.Percent).HasColumnType("decimal(9,4)");
        b.HasIndex(c => new { c.TaxRateId, c.Code }).IsUnique().HasDatabaseName("UX_TaxRateComponent_Code");
    }
}

public sealed class TenantTaxSettingConfiguration : IEntityTypeConfiguration<TenantTaxSetting>
{
    public void Configure(EntityTypeBuilder<TenantTaxSetting> b)
    {
        b.ToTable("TenantTaxSetting");
        b.HasKey(s => s.TenantTaxSettingId);
        b.Property(s => s.RoundingMode).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(s => s.DefaultRateCode).HasMaxLength(30).IsUnicode(false);
        b.HasIndex(s => s.TenantId).IsUnique().HasDatabaseName("UX_TenantTaxSetting_Tenant");
    }
}

public sealed class TaxChargeRuleConfiguration : IEntityTypeConfiguration<TaxChargeRule>
{
    public void Configure(EntityTypeBuilder<TaxChargeRule> b)
    {
        b.ToTable("TaxChargeRule");
        b.HasKey(r => r.TaxChargeRuleId);
        b.HasIndex(r => new { r.TenantId, r.ChargeTypeLookupId }).IsUnique().HasDatabaseName("UX_TaxChargeRule");
    }
}

public sealed class ClientTaxExemptionConfiguration : IEntityTypeConfiguration<ClientTaxExemption>
{
    public void Configure(EntityTypeBuilder<ClientTaxExemption> b)
    {
        b.ToTable("ClientTaxExemption");
        b.HasKey(e => e.ClientTaxExemptionId);
        b.Property(e => e.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(e => e.ExemptionType).HasMaxLength(60).IsRequired();
        b.Property(e => e.CertificateNumber).HasMaxLength(60).IsRequired();
        b.Property(e => e.ValidFrom).HasColumnType("date");
        b.Property(e => e.ValidTo).HasColumnType("date");
        b.Property(e => e.Notes).HasMaxLength(300);
        b.Property(e => e.Scope).HasMaxLength(20).IsRequired();          // Lote 237
        b.Property(e => e.ComponentCodes).HasMaxLength(310);
        b.HasIndex(e => e.PublicId).IsUnique().HasDatabaseName("UQ_ClientTaxExemption_PublicId");
        b.HasIndex(e => new { e.TenantId, e.ClientId, e.ValidFrom }).HasDatabaseName("IX_ClientTaxExemption_Client");
        // Lote 248: el documento que la respalda. EF mapea la FK simple; la compuesta FK_ClientTaxExemption_Document (ClientDocumentId, TenantId, ClientId) vive en el SQL.
        // Un documento respalda a lo más UNA exención activa (índice único filtrado: la segunda que lo intente recibe 409).
        b.HasOne<Teikem.Domain.Clients.ClientDocument>().WithMany().HasForeignKey(e => e.ClientDocumentId).IsRequired(false);
        b.HasIndex(e => e.ClientDocumentId).IsUnique().HasFilter("[ClientDocumentId] IS NOT NULL AND [IsActive] = 1").HasDatabaseName("UX_ClientTaxExemption_Document");
    }
}

// Lote 248 — exención de producto por temporada. Mapeo 1:1 con la sección del Lote 248 de Diseño/logistica-db-update.sql. Los CHECK (fechas, alcance, nombre y motivo no
// vacíos), el UNIQUE (Id, TenantId) y las FK compuestas con la compañía (período, producto y categoría) viven en el SQL; EF mapea las FK simples.

public sealed class TaxExemptionPeriodConfiguration : IEntityTypeConfiguration<TaxExemptionPeriod>
{
    public const int NameMaxLength = 100;
    public const int ReasonMaxLength = 300;

    public void Configure(EntityTypeBuilder<TaxExemptionPeriod> b)
    {
        b.ToTable("TaxExemptionPeriod");
        b.HasKey(p => p.TaxExemptionPeriodId);
        b.Property(p => p.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(p => p.Name).HasMaxLength(NameMaxLength).IsRequired();
        b.Property(p => p.Reason).HasMaxLength(ReasonMaxLength).IsRequired();
        b.Property(p => p.ValidFrom).HasColumnType("date");
        b.Property(p => p.ValidTo).HasColumnType("date");
        b.Property(p => p.Scope).HasMaxLength(20).IsRequired();
        b.Property(p => p.RowVersion).IsRowVersion();
        b.HasMany(p => p.Products).WithOne().HasForeignKey(x => x.TaxExemptionPeriodId);
        b.HasMany(p => p.Categories).WithOne().HasForeignKey(x => x.TaxExemptionPeriodId);
        b.HasIndex(p => p.PublicId).IsUnique().HasDatabaseName("UX_TaxExemptionPeriod_PublicId");
        b.HasIndex(p => new { p.TenantId, p.ValidFrom, p.ValidTo }).HasFilter("[IsActive] = 1").HasDatabaseName("IX_TaxExemptionPeriod_Dates");
    }
}

public sealed class TaxExemptionPeriodProductConfiguration : IEntityTypeConfiguration<TaxExemptionPeriodProduct>
{
    public void Configure(EntityTypeBuilder<TaxExemptionPeriodProduct> b)
    {
        b.ToTable("TaxExemptionPeriodProduct");
        b.HasKey(x => x.TaxExemptionPeriodProductId);
        b.HasOne<Teikem.Domain.Wms.Product>().WithMany().HasForeignKey(x => x.ProductId);
        b.HasIndex(x => new { x.TaxExemptionPeriodId, x.ProductId }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_TaxExemptionPeriodProduct");
    }
}

public sealed class TaxExemptionPeriodCategoryConfiguration : IEntityTypeConfiguration<TaxExemptionPeriodCategory>
{
    public void Configure(EntityTypeBuilder<TaxExemptionPeriodCategory> b)
    {
        b.ToTable("TaxExemptionPeriodCategory");
        b.HasKey(x => x.TaxExemptionPeriodCategoryId);
        b.HasOne<Teikem.Domain.Wms.ProductCategory>().WithMany().HasForeignKey(x => x.ProductCategoryId);
        b.HasIndex(x => new { x.TaxExemptionPeriodId, x.ProductCategoryId }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_TaxExemptionPeriodCategory");
    }
}
