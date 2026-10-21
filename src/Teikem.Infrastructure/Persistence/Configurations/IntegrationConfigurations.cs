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

public sealed class IntegrationApiKeyConfiguration : IEntityTypeConfiguration<IntegrationApiKey>
{
    public void Configure(EntityTypeBuilder<IntegrationApiKey> b)
    {
        b.ToTable("IntegrationApiKey");
        b.HasKey(k => k.IntegrationApiKeyId);
        b.Property(k => k.KeyId).HasMaxLength(40).IsUnicode(false).IsRequired();
        b.Property(k => k.SecretHash).HasMaxLength(64).IsUnicode(false).IsFixedLength().IsRequired();
        b.Property(k => k.SecretLast4).HasMaxLength(4).IsUnicode(false).IsRequired();
        b.Property(k => k.Label).HasMaxLength(80).IsRequired();
        b.HasIndex(k => k.KeyId).IsUnique().HasDatabaseName("UQ_IntegrationApiKey_KeyId");
        b.HasIndex(k => new { k.TenantId, k.IntegrationId }).HasDatabaseName("IX_IntegrationApiKey_Integration");
    }
}

public sealed class IntegrationApiLogConfiguration : IEntityTypeConfiguration<IntegrationApiLog>
{
    public void Configure(EntityTypeBuilder<IntegrationApiLog> b)
    {
        b.ToTable("IntegrationApiLog");
        b.HasKey(l => l.IntegrationApiLogId);
        b.Property(l => l.Method).HasMaxLength(8).IsUnicode(false).IsRequired();
        b.Property(l => l.Path).HasMaxLength(300).IsRequired();
        b.HasIndex(l => new { l.TenantId, l.IntegrationApiKeyId, l.IntegrationApiLogId }).HasDatabaseName("IX_IntegrationApiLog_Key");
    }
}

public sealed class IntegrationWebhookConfiguration : IEntityTypeConfiguration<IntegrationWebhook>
{
    public void Configure(EntityTypeBuilder<IntegrationWebhook> b)
    {
        b.ToTable("IntegrationWebhook");
        b.HasKey(w => w.IntegrationWebhookId);
        b.Property(w => w.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(w => w.Url).HasMaxLength(500).IsRequired();
        b.Property(w => w.EventsJson).HasMaxLength(400).IsRequired();
        b.Property(w => w.SecretEnc).HasMaxLength(400).IsRequired();
        b.Property(w => w.SecretLast4).HasMaxLength(4).IsUnicode(false).IsRequired();
        b.Property(w => w.LastStatus).HasMaxLength(12).IsUnicode(false);
        b.HasIndex(w => w.PublicId).IsUnique().HasDatabaseName("UQ_IntegrationWebhook_Public");
        b.HasIndex(w => new { w.TenantId, w.IntegrationId }).HasDatabaseName("IX_IntegrationWebhook_Integration");
    }
}

public sealed class IntegrationWebhookDeliveryConfiguration : IEntityTypeConfiguration<IntegrationWebhookDelivery>
{
    public void Configure(EntityTypeBuilder<IntegrationWebhookDelivery> b)
    {
        b.ToTable("IntegrationWebhookDelivery");
        b.HasKey(d => d.IntegrationWebhookDeliveryId);
        b.Property(d => d.EventType).HasMaxLength(40).IsUnicode(false).IsRequired();
        b.Property(d => d.PayloadJson).IsRequired();
        b.Property(d => d.Status).HasMaxLength(12).IsUnicode(false).IsRequired();
        b.Property(d => d.Error).HasMaxLength(500);
        b.HasIndex(d => new { d.TenantId, d.Status, d.NextAttemptUtc }).HasDatabaseName("IX_IntegrationWebhookDelivery_Due");
        b.HasIndex(d => new { d.TenantId, d.IntegrationWebhookId, d.IntegrationWebhookDeliveryId }).HasDatabaseName("IX_IntegrationWebhookDelivery_Webhook");
    }
}

public sealed class IntegrationOutputConfiguration : IEntityTypeConfiguration<IntegrationOutput>
{
    public void Configure(EntityTypeBuilder<IntegrationOutput> b)
    {
        b.ToTable("IntegrationOutput");
        b.HasKey(o => o.IntegrationOutputId);
        b.Property(o => o.FileName).HasMaxLength(160).IsRequired();
        b.Property(o => o.Format).HasMaxLength(4).IsUnicode(false).IsRequired();
        b.Property(o => o.Content).IsRequired();
        b.Property(o => o.Status).HasMaxLength(12).IsUnicode(false).IsRequired();
        b.Property(o => o.Error).HasMaxLength(500);
        b.HasIndex(o => new { o.TenantId, o.Status, o.NextAttemptUtc }).HasDatabaseName("IX_IntegrationOutput_Due");
        b.HasIndex(o => new { o.TenantId, o.IntegrationId, o.IntegrationOutputId }).HasDatabaseName("IX_IntegrationOutput_Integration");
    }
}

public sealed class IntegrationOutputEntityConfiguration : IEntityTypeConfiguration<IntegrationOutputEntity>
{
    public void Configure(EntityTypeBuilder<IntegrationOutputEntity> b)
    {
        b.ToTable("IntegrationOutputEntity");
        b.HasKey(o => o.IntegrationOutputEntityId);
        b.HasIndex(o => new { o.TenantId, o.IntegrationId, o.IntegrationOutputId }).HasDatabaseName("IX_IntegrationOutputEntity_Queue");
    }
}

public sealed class IntegrationOutputOrderConfiguration : IEntityTypeConfiguration<IntegrationOutputOrder>
{
    public void Configure(EntityTypeBuilder<IntegrationOutputOrder> b)
    {
        b.ToTable("IntegrationOutputOrder");
        b.HasKey(o => o.IntegrationOutputOrderId);
        b.Property(o => o.StatusCode).HasMaxLength(30).IsUnicode(false).IsRequired();
        b.HasIndex(o => new { o.TenantId, o.IntegrationId, o.IntegrationOutputId }).HasDatabaseName("IX_IntegrationOutputOrder_Queue");
    }
}
