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
        b.HasIndex(e => e.PublicId).IsUnique().HasDatabaseName("UQ_ClientTaxExemption_PublicId");
        b.HasIndex(e => new { e.TenantId, e.ClientId, e.ValidFrom }).HasDatabaseName("IX_ClientTaxExemption_Client");
    }
}
