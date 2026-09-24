using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Analytics;
using Teikem.Domain.Contacts;
using Teikem.Domain.CustomFields;

namespace Teikem.Infrastructure.Persistence.Configurations;

public sealed class ContactPointConfiguration : IEntityTypeConfiguration<ContactPoint>
{
    public void Configure(EntityTypeBuilder<ContactPoint> b)
    {
        b.ToTable("ContactPoint");
        b.HasKey(c => c.ContactPointId);
        b.Property(c => c.Value).HasMaxLength(200).IsRequired();
        b.Property(c => c.Extension).HasMaxLength(20);
        b.Property(c => c.Label).HasMaxLength(80);
        b.HasOne(c => c.OwnerEntity).WithMany().HasForeignKey(c => c.OwnerEntityLookupId);
        b.HasOne(c => c.ContactType).WithMany().HasForeignKey(c => c.ContactTypeLookupId);
        b.HasIndex(c => new { c.OwnerEntityLookupId, c.OwnerId });
    }
}

public sealed class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> b)
    {
        b.ToTable("CustomFieldDefinition");
        b.HasKey(d => d.CustomFieldDefinitionId);
        b.Property(d => d.FieldKey).HasMaxLength(60).IsRequired();
        b.Property(d => d.DefaultValue).HasMaxLength(400);
        b.Property(d => d.RefEntity).HasMaxLength(60);
        b.Property(d => d.RowVersion).IsRowVersion();
        b.HasIndex(d => new { d.TenantId, d.EntityTypeLookupId, d.FieldKey }).IsUnique();
        b.HasOne(d => d.EntityType).WithMany().HasForeignKey(d => d.EntityTypeLookupId);
        b.HasOne(d => d.DataType).WithMany().HasForeignKey(d => d.DataTypeLookupId);
        b.HasMany(d => d.Options).WithOne(o => o.Definition).HasForeignKey(o => o.CustomFieldDefinitionId);
    }
}

public sealed class CustomFieldOptionConfiguration : IEntityTypeConfiguration<CustomFieldOption>
{
    public void Configure(EntityTypeBuilder<CustomFieldOption> b)
    {
        b.ToTable("CustomFieldOption");
        b.HasKey(o => o.CustomFieldOptionId);
        b.Property(o => o.OptionValue).HasMaxLength(80).IsRequired();
        b.HasIndex(o => new { o.CustomFieldDefinitionId, o.OptionValue }).IsUnique();
    }
}

public sealed class CustomFieldValueConfiguration : IEntityTypeConfiguration<CustomFieldValue>
{
    public void Configure(EntityTypeBuilder<CustomFieldValue> b)
    {
        b.ToTable("CustomFieldValue");
        b.HasKey(v => v.CustomFieldValueId);
        b.Property(v => v.ValueNumber).HasColumnType("decimal(18,4)");
        b.HasIndex(v => new { v.CustomFieldDefinitionId, v.EntityId }).IsUnique();
        b.HasOne(v => v.Definition).WithMany().HasForeignKey(v => v.CustomFieldDefinitionId);
    }
}

public sealed class ReportDefinitionConfiguration : IEntityTypeConfiguration<ReportDefinition>
{
    public void Configure(EntityTypeBuilder<ReportDefinition> b)
    {
        b.ToTable("ReportDefinition");
        b.HasKey(r => r.ReportDefinitionId);
        b.Property(r => r.PublicId).HasDefaultValueSql("NEWID()");
        b.Property(r => r.Name).HasMaxLength(150).IsRequired();
        b.Property(r => r.ScheduleCron).HasMaxLength(60);
        b.Property(r => r.DeliveryEmails).HasMaxLength(400);
        b.Property(r => r.RowVersion).IsRowVersion();
        b.HasIndex(r => new { r.TenantId, r.BaseEntityTypeLookupId, r.Name }).IsUnique();
        b.HasOne(r => r.BaseEntityType).WithMany().HasForeignKey(r => r.BaseEntityTypeLookupId);
        b.HasOne(r => r.Visibility).WithMany().HasForeignKey(r => r.VisibilityLookupId);
        b.HasMany(r => r.Shares).WithOne(s => s.Report).HasForeignKey(s => s.ReportDefinitionId);
    }
}

public sealed class ReportShareConfiguration : IEntityTypeConfiguration<ReportShare>
{
    public void Configure(EntityTypeBuilder<ReportShare> b)
    {
        b.ToTable("ReportShare");
        b.HasKey(s => s.ReportShareId);
    }
}

public sealed class IndicatorDefinitionConfiguration : IEntityTypeConfiguration<IndicatorDefinition>
{
    public void Configure(EntityTypeBuilder<IndicatorDefinition> b)
    {
        b.ToTable("IndicatorDefinition");
        b.HasKey(i => i.IndicatorDefinitionId);
        b.Property(i => i.PublicId).HasDefaultValueSql("NEWID()");
        b.Property(i => i.Name).HasMaxLength(150).IsRequired();
        b.Property(i => i.DataSourceKey).HasMaxLength(60).IsRequired();
        b.Property(i => i.FieldKey).HasMaxLength(80);
        b.Property(i => i.DateFrom).HasColumnType("date");
        b.Property(i => i.DateTo).HasColumnType("date");
        b.Property(i => i.RowVersion).IsRowVersion();
        b.HasIndex(i => new { i.TenantId, i.Name }).IsUnique();
        b.HasMany(i => i.Shares).WithOne(s => s.Indicator).HasForeignKey(s => s.IndicatorDefinitionId);
    }
}

public sealed class IndicatorShareConfiguration : IEntityTypeConfiguration<IndicatorShare>
{
    public void Configure(EntityTypeBuilder<IndicatorShare> b)
    {
        b.ToTable("IndicatorShare");
        b.HasKey(s => s.IndicatorShareId);
    }
}

public sealed class ChartDefinitionConfiguration : IEntityTypeConfiguration<ChartDefinition>
{
    public void Configure(EntityTypeBuilder<ChartDefinition> b)
    {
        b.ToTable("ChartDefinition");
        b.HasKey(c => c.ChartDefinitionId);
        b.Property(c => c.PublicId).HasDefaultValueSql("NEWID()");
        b.Property(c => c.Name).HasMaxLength(150).IsRequired();
        b.Property(c => c.DataSourceKey).HasMaxLength(60).IsRequired();
        b.Property(c => c.GroupByField).HasMaxLength(80).IsRequired();
        b.Property(c => c.FieldKey).HasMaxLength(80);
        b.Property(c => c.DateFrom).HasColumnType("date");
        b.Property(c => c.DateTo).HasColumnType("date");
        b.Property(c => c.RowVersion).IsRowVersion();
        b.HasIndex(c => new { c.TenantId, c.Name }).IsUnique();
        b.HasMany(c => c.Shares).WithOne(s => s.Chart).HasForeignKey(s => s.ChartDefinitionId);
    }
}

public sealed class ChartShareConfiguration : IEntityTypeConfiguration<ChartShare>
{
    public void Configure(EntityTypeBuilder<ChartShare> b)
    {
        b.ToTable("ChartShare");
        b.HasKey(s => s.ChartShareId);
    }
}

public sealed class UserAnalyticsPreferenceConfiguration : IEntityTypeConfiguration<UserAnalyticsPreference>
{
    public void Configure(EntityTypeBuilder<UserAnalyticsPreference> b)
    {
        b.ToTable("UserAnalyticsPreference");
        b.HasKey(p => p.UserAnalyticsPreferenceId);
        b.Property(p => p.DateFrom).HasColumnType("date");
        b.Property(p => p.DateTo).HasColumnType("date");
    }
}
