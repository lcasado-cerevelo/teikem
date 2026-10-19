using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Integrations;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 132 (P11) — integraciones. Mapeo 1:1 con la sección «2026-10-19 (bj)» de Diseño/logistica-db-update.sql (dbo.Integration, IntegrationRun, IntegrationFileLog, IntegrationInboxItem).
// Los CHECK viven en SQL; aquí se espejan los índices con el mismo nombre.

public sealed class IntegrationConfiguration : IEntityTypeConfiguration<Integration>
{
    public void Configure(EntityTypeBuilder<Integration> b)
    {
        b.ToTable("Integration");
        b.HasKey(i => i.IntegrationId);
        b.Property(i => i.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(i => i.Name).HasMaxLength(120).IsRequired();
        b.Property(i => i.Description).HasMaxLength(400);
        b.Property(i => i.Direction).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(i => i.ConnectorKind).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.Property(i => i.ScheduleKind).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(i => i.ScopesJson).IsRequired();
        b.Property(i => i.NotifyUserIdsJson).IsRequired();
        b.Property(i => i.LastStatus).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(i => i.LastMessage).HasMaxLength(400);
        b.Property(i => i.LockedBy).HasMaxLength(80);
        b.Property(i => i.RowVersion).IsRowVersion();
        b.HasOne(i => i.ImportTemplate).WithMany().HasForeignKey(i => i.ImportTemplateId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(i => i.ServiceUser).WithMany().HasForeignKey(i => i.ServiceUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(i => i.ServiceRole).WithMany().HasForeignKey(i => i.ServiceRoleId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(i => new { i.TenantId, i.Name }).IsUnique().HasDatabaseName("UQ_Integration_Name");
        b.HasIndex(i => i.PublicId).IsUnique().HasDatabaseName("UQ_Integration_PublicId");
    }
}

public sealed class IntegrationRunConfiguration : IEntityTypeConfiguration<IntegrationRun>
{
    public void Configure(EntityTypeBuilder<IntegrationRun> b)
    {
        b.ToTable("IntegrationRun");
        b.HasKey(r => r.IntegrationRunId);
        b.Property(r => r.Status).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(r => r.TriggeredBy).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(r => r.Message).HasMaxLength(1000);
        b.HasIndex(r => new { r.TenantId, r.IntegrationId, r.IntegrationRunId }).HasDatabaseName("IX_IntegrationRun_Integration");
    }
}

public sealed class IntegrationFileLogConfiguration : IEntityTypeConfiguration<IntegrationFileLog>
{
    public void Configure(EntityTypeBuilder<IntegrationFileLog> b)
    {
        b.ToTable("IntegrationFileLog");
        b.HasKey(f => f.IntegrationFileLogId);
        b.Property(f => f.FileName).HasMaxLength(260).IsRequired();
        b.Property(f => f.Sha256).HasMaxLength(64).IsUnicode(false).IsFixedLength().IsRequired();
        b.Property(f => f.Result).HasMaxLength(12).IsUnicode(false).IsRequired();
        b.Property(f => f.Message).HasMaxLength(1000);
        b.HasIndex(f => new { f.IntegrationId, f.Sha256 }).IsUnique().HasFilter("[Result] = 'PROCESSED'").HasDatabaseName("UX_IntegrationFileLog_Hash");
    }
}

public sealed class IntegrationInboxItemConfiguration : IEntityTypeConfiguration<IntegrationInboxItem>
{
    public void Configure(EntityTypeBuilder<IntegrationInboxItem> b)
    {
        b.ToTable("IntegrationInboxItem");
        b.HasKey(i => i.IntegrationInboxItemId);
        b.Property(i => i.ReferenceKey).HasMaxLength(80);
        b.Property(i => i.Reason).HasMaxLength(1000).IsRequired();
        b.Property(i => i.Status).HasMaxLength(12).IsUnicode(false).IsRequired();
        b.Property(i => i.Note).HasMaxLength(400);
        b.Property(i => i.RowVersion).IsRowVersion();
        b.HasIndex(i => new { i.TenantId, i.IntegrationId, i.Status, i.IntegrationInboxItemId }).HasDatabaseName("IX_IntegrationInbox_Status");
    }
}
