using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Catalogs;

namespace Teikem.Infrastructure.Persistence.Configurations;

public sealed class CatalogDomainConfiguration : IEntityTypeConfiguration<CatalogDomain>
{
    public void Configure(EntityTypeBuilder<CatalogDomain> b)
    {
        b.ToTable("CatalogDomain");
        b.HasKey(d => d.CatalogDomainId);
        b.Property(d => d.DomainKey).HasMaxLength(60).IsRequired();
        b.Property(d => d.Description).HasMaxLength(300);
        b.HasIndex(d => d.DomainKey).IsUnique();
    }
}

public sealed class LookupCodeConfiguration : IEntityTypeConfiguration<LookupCode>
{
    public void Configure(EntityTypeBuilder<LookupCode> b)
    {
        b.ToTable("LookupCode");
        b.HasKey(l => l.LookupCodeId);
        b.Property(l => l.Entity).HasMaxLength(60).IsRequired();
        b.Property(l => l.InternalCode).HasMaxLength(40).IsRequired();
        b.Property(l => l.RowVersion).IsRowVersion();
        b.HasIndex(l => new { l.Entity, l.InternalCode }).IsUnique();
        b.HasOne(l => l.Domain).WithMany().HasForeignKey(l => l.Entity).HasPrincipalKey(d => d.DomainKey);
        b.HasMany(l => l.Overrides).WithOne(o => o.LookupCode).HasForeignKey(o => o.LookupCodeId);
    }
}

public sealed class LookupCodeOverrideConfiguration : IEntityTypeConfiguration<LookupCodeOverride>
{
    public void Configure(EntityTypeBuilder<LookupCodeOverride> b)
    {
        b.ToTable("LookupCodeOverride");
        b.HasKey(o => o.LookupCodeOverrideId);
        b.HasIndex(o => new { o.TenantId, o.LookupCodeId }).IsUnique();
    }
}

public sealed class StatusCodeConfiguration : IEntityTypeConfiguration<StatusCode>
{
    public void Configure(EntityTypeBuilder<StatusCode> b)
    {
        b.ToTable("StatusCode");
        b.HasKey(s => s.StatusCodeId);
        b.Property(s => s.Entity).HasMaxLength(60).IsRequired();
        b.Property(s => s.InternalCode).HasMaxLength(40).IsRequired();
        b.Property(s => s.ColorHex).HasColumnType("char(7)");
        b.Property(s => s.Icon).HasMaxLength(40);
        b.Property(s => s.RowVersion).IsRowVersion();
        b.HasIndex(s => new { s.Entity, s.InternalCode }).IsUnique();
        b.HasOne(s => s.Domain).WithMany().HasForeignKey(s => s.Entity).HasPrincipalKey(d => d.DomainKey);
        b.HasOne(s => s.StageKind).WithMany().HasForeignKey(s => s.StageKindLookupId);
        b.HasMany(s => s.Overrides).WithOne(o => o.StatusCode).HasForeignKey(o => o.StatusCodeId);
    }
}

public sealed class StatusCodeOverrideConfiguration : IEntityTypeConfiguration<StatusCodeOverride>
{
    public void Configure(EntityTypeBuilder<StatusCodeOverride> b)
    {
        b.ToTable("StatusCodeOverride");
        b.HasKey(o => o.StatusCodeOverrideId);
        b.Property(o => o.CustomColorHex).HasColumnType("char(7)");
        b.HasIndex(o => new { o.TenantId, o.StatusCodeId }).IsUnique();
    }
}

public sealed class StatusLateralEntryConfiguration : IEntityTypeConfiguration<StatusLateralEntry>
{
    public void Configure(EntityTypeBuilder<StatusLateralEntry> b)
    {
        b.ToTable("StatusLateralEntry");
        b.HasKey(s => s.StatusLateralEntryId);
        b.HasOne(s => s.EntityType).WithMany().HasForeignKey(s => s.EntityTypeLookupId);
        b.HasOne(s => s.LateralStatus).WithMany().HasForeignKey(s => s.LateralStatusCodeId);
        b.HasOne(s => s.FromStatus).WithMany().HasForeignKey(s => s.FromStatusCodeId);
    }
}

public sealed class StatusCapabilityConfiguration : IEntityTypeConfiguration<StatusCapability>
{
    public void Configure(EntityTypeBuilder<StatusCapability> b)
    {
        b.ToTable("StatusCapability");
        b.HasKey(s => s.StatusCapabilityId);
        b.HasOne(s => s.EntityType).WithMany().HasForeignKey(s => s.EntityTypeLookupId);
        b.HasOne(s => s.StatusCode).WithMany().HasForeignKey(s => s.StatusCodeId);
        b.HasOne(s => s.Capability).WithMany().HasForeignKey(s => s.CapabilityLookupId);
    }
}

public sealed class EntityStatusHistoryConfiguration : IEntityTypeConfiguration<EntityStatusHistory>
{
    public void Configure(EntityTypeBuilder<EntityStatusHistory> b)
    {
        b.ToTable("EntityStatusHistory");
        b.HasKey(h => h.EntityStatusHistoryId);
        b.Property(h => h.Comment).HasMaxLength(500);
        b.HasOne(h => h.EntityType).WithMany().HasForeignKey(h => h.EntityTypeLookupId);
        b.HasOne(h => h.FromStatus).WithMany().HasForeignKey(h => h.FromStatusCodeId);
        b.HasOne(h => h.ToStatus).WithMany().HasForeignKey(h => h.ToStatusCodeId);
        b.HasIndex(h => new { h.EntityTypeLookupId, h.EntityId });
    }
}
