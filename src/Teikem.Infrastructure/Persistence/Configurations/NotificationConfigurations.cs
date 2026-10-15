using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Notifications;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 51 — avisos. Mapeo 1:1 con la sección «2026-10-14 (g)» de Diseño/logistica-db-update.sql. Los CHECK viven en SQL; aquí se
// espejan los índices con el mismo nombre y filtro.

public sealed class NotificationRuleConfiguration : IEntityTypeConfiguration<NotificationRule>
{
    public void Configure(EntityTypeBuilder<NotificationRule> b)
    {
        b.ToTable("NotificationRule");
        b.HasKey(r => r.NotificationRuleId);
        b.Property(r => r.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(r => r.EventCode).HasMaxLength(60).IsUnicode(false).IsRequired();
        b.Property(r => r.Name).HasMaxLength(100).IsRequired();
        b.Property(r => r.RowVersion).IsRowVersion();
        b.HasMany(r => r.Channels).WithOne().HasForeignKey(c => c.NotificationRuleId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(r => r.Recipients).WithOne().HasForeignKey(c => c.NotificationRuleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r => r.PublicId).IsUnique().HasDatabaseName("UQ_NotificationRule_PublicId");
        b.HasIndex(r => new { r.TenantId, r.Name }).IsUnique().HasDatabaseName("UX_NotificationRule_Name");
        b.HasIndex(r => new { r.TenantId, r.EventCode, r.IsActive }).HasDatabaseName("IX_NotificationRule_Event");
    }
}

public sealed class NotificationRuleChannelConfiguration : IEntityTypeConfiguration<NotificationRuleChannel>
{
    public void Configure(EntityTypeBuilder<NotificationRuleChannel> b)
    {
        b.ToTable("NotificationRuleChannel");
        b.HasKey(c => c.NotificationRuleChannelId);
        b.Property(c => c.Channel).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(c => c.SubjectJson).HasMaxLength(1000);
        b.Property(c => c.BodyJson).HasMaxLength(4000);
        b.HasIndex(c => new { c.NotificationRuleId, c.Channel }).IsUnique().HasDatabaseName("UX_NotificationRuleChannel");
    }
}

public sealed class NotificationRuleRecipientConfiguration : IEntityTypeConfiguration<NotificationRuleRecipient>
{
    public void Configure(EntityTypeBuilder<NotificationRuleRecipient> b)
    {
        b.ToTable("NotificationRuleRecipient");
        b.HasKey(c => c.NotificationRuleRecipientId);
        b.Property(c => c.Kind).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(c => c.Value).HasMaxLength(200);
        b.HasIndex(c => c.NotificationRuleId).HasDatabaseName("IX_NotificationRuleRecipient_Rule");
    }
}

public sealed class NotificationOutboxConfiguration : IEntityTypeConfiguration<NotificationOutbox>
{
    public void Configure(EntityTypeBuilder<NotificationOutbox> b)
    {
        b.ToTable("NotificationOutbox");
        b.HasKey(o => o.NotificationOutboxId);
        b.Property(o => o.EventCode).HasMaxLength(60).IsUnicode(false).IsRequired();
        b.Property(o => o.PayloadJson).HasMaxLength(4000);
        b.Property(o => o.DedupKey).HasMaxLength(100).IsUnicode(false);
        b.Property(o => o.Status).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(o => o.LockedBy).HasMaxLength(80);
        b.Property(o => o.LastError).HasMaxLength(1000);
        b.HasIndex(o => new { o.Status, o.NextAttemptUtc }).HasDatabaseName("IX_NotificationOutbox_Due");
        b.HasIndex(o => new { o.TenantId, o.EventCode, o.DedupKey }).IsUnique().HasFilter("[DedupKey] IS NOT NULL").HasDatabaseName("UX_NotificationOutbox_Dedup");
    }
}

public sealed class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> b)
    {
        b.ToTable("NotificationLog");
        b.HasKey(l => l.NotificationLogId);
        b.Property(l => l.EventCode).HasMaxLength(60).IsUnicode(false).IsRequired();
        b.Property(l => l.Channel).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(l => l.Recipient).HasMaxLength(200).IsRequired();
        b.Property(l => l.Status).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(l => l.Error).HasMaxLength(1000);
        b.HasIndex(l => new { l.NotificationOutboxId, l.NotificationRuleId, l.Channel, l.Recipient }).IsUnique().HasDatabaseName("UX_NotificationLog_Delivery");
        b.HasIndex(l => new { l.TenantId, l.CreatedAtUtc }).HasDatabaseName("IX_NotificationLog_Tenant");
    }
}

public sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> b)
    {
        b.ToTable("UserNotification");
        b.HasKey(n => n.UserNotificationId);
        b.Property(n => n.EventCode).HasMaxLength(60).IsUnicode(false).IsRequired();
        b.Property(n => n.Title).HasMaxLength(200).IsRequired();
        b.Property(n => n.Body).HasMaxLength(1000);
        b.Property(n => n.LinkUrl).HasMaxLength(300);
        b.HasIndex(n => new { n.TenantId, n.UserId, n.IsRead, n.UserNotificationId }).HasDatabaseName("IX_UserNotification_Inbox");
        b.HasIndex(n => n.AnnouncementId).HasFilter("[AnnouncementId] IS NOT NULL").HasDatabaseName("IX_UserNotification_Announcement");
        b.HasIndex(n => new { n.NotificationOutboxId, n.NotificationRuleId, n.UserId }).IsUnique()
            .HasFilter("[NotificationOutboxId] IS NOT NULL AND [NotificationRuleId] IS NOT NULL").HasDatabaseName("UX_UserNotification_Delivery");
    }
}

public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> b)
    {
        b.ToTable("Announcement");
        b.HasKey(a => a.AnnouncementId);
        b.Property(a => a.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(a => a.Title).HasMaxLength(150).IsRequired();
        b.Property(a => a.Body).HasMaxLength(1000);
        b.Property(a => a.AudienceText).HasMaxLength(300).IsRequired();
        b.HasIndex(a => a.PublicId).IsUnique().HasDatabaseName("UQ_Announcement_PublicId");
        b.HasIndex(a => new { a.TenantId, a.AnnouncementId }).HasDatabaseName("IX_Announcement_Tenant");
    }
}

public sealed class UserNotificationPrefConfiguration : IEntityTypeConfiguration<UserNotificationPref>
{
    public void Configure(EntityTypeBuilder<UserNotificationPref> b)
    {
        b.ToTable("UserNotificationPref");
        b.HasKey(p => p.UserNotificationPrefId);
        b.Property(p => p.EventCode).HasMaxLength(60).IsUnicode(false).IsRequired();
        b.HasIndex(p => new { p.TenantId, p.UserId, p.EventCode }).IsUnique().HasDatabaseName("UX_UserNotificationPref");
    }
}
