using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Identity;
using Teikem.Domain.Tenancy;

namespace Teikem.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.ToTable("AspNetUsers");
        b.Property(u => u.FullName).HasMaxLength(150);
        b.Property(u => u.PhoneNumber).HasMaxLength(50);
        b.HasOne(u => u.DefaultTenant).WithMany().HasForeignKey(u => u.DefaultTenantId);
        b.HasOne(u => u.UserKind).WithMany().HasForeignKey(u => u.UserKindLookupId);
        b.HasMany(u => u.Memberships).WithOne(m => m.User).HasForeignKey(m => m.UserId);
    }
}

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("Tenant");
        b.HasKey(t => t.TenantId);
        b.Property(t => t.PublicId).HasDefaultValueSql("NEWID()");
        b.Property(t => t.Name).HasMaxLength(200).IsRequired();
        b.Property(t => t.LegalName).HasMaxLength(250);
        b.Property(t => t.TaxId).HasMaxLength(50);
        b.Property(t => t.DefaultLangCode).HasColumnType("char(2)").IsRequired();
        b.Property(t => t.RowVersion).IsRowVersion();
        b.HasMany(t => t.Holidays).WithOne(h => h.Tenant).HasForeignKey(h => h.TenantId);
        b.HasMany(t => t.Modules).WithOne(m => m.Tenant).HasForeignKey(m => m.TenantId);
    }
}

public sealed class TenantHolidayConfiguration : IEntityTypeConfiguration<TenantHoliday>
{
    public void Configure(EntityTypeBuilder<TenantHoliday> b)
    {
        b.ToTable("TenantHoliday");
        b.HasKey(h => h.TenantHolidayId);
        b.Property(h => h.Name).HasMaxLength(120).IsRequired();
        b.Property(h => h.HolidayDate).HasColumnType("date");
        b.HasIndex(h => new { h.TenantId, h.HolidayDate }).IsUnique();
    }
}

public sealed class ModuleDefinitionConfiguration : IEntityTypeConfiguration<ModuleDefinition>
{
    public void Configure(EntityTypeBuilder<ModuleDefinition> b)
    {
        b.ToTable("ModuleDefinition");
        b.HasKey(m => m.ModuleKey);
        b.Property(m => m.ModuleKey).HasColumnType("varchar(40)");
        b.Property(m => m.DependsOnModuleKey).HasColumnType("varchar(40)");
        b.Property(m => m.Name).HasMaxLength(120).IsRequired();
        b.Property(m => m.NameEn).HasMaxLength(120).IsRequired();
        b.Property(m => m.Description).HasMaxLength(300);
        b.Property(m => m.Category).HasMaxLength(40).IsRequired();
        b.HasOne(m => m.DependsOn).WithMany().HasForeignKey(m => m.DependsOnModuleKey);
    }
}

public sealed class TenantModuleConfiguration : IEntityTypeConfiguration<TenantModule>
{
    public void Configure(EntityTypeBuilder<TenantModule> b)
    {
        b.ToTable("TenantModule");
        b.HasKey(m => new { m.TenantId, m.ModuleKey });
        b.Property(m => m.ModuleKey).HasColumnType("varchar(40)");
        b.HasOne(m => m.Module).WithMany().HasForeignKey(m => m.ModuleKey);
    }
}
