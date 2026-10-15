using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Messaging;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 53 — mensajería. Mapeo 1:1 con la sección «2026-10-15 (h)» de Diseño/logistica-db-update.sql. Los CHECK viven en SQL;
// aquí se espejan los índices con el mismo nombre y filtro.

public sealed class MessageThreadConfiguration : IEntityTypeConfiguration<MessageThread>
{
    public void Configure(EntityTypeBuilder<MessageThread> b)
    {
        b.ToTable("MessageThread");
        b.HasKey(t => t.MessageThreadId);
        b.Property(t => t.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(t => t.Kind).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(t => t.Title).HasMaxLength(100);
        b.Property(t => t.DirectKey).HasMaxLength(40).IsUnicode(false);
        b.Property(t => t.LastMessagePreview).HasMaxLength(200);
        b.HasIndex(t => t.PublicId).IsUnique().HasDatabaseName("UQ_MessageThread_PublicId");
        b.HasIndex(t => new { t.TenantId, t.DirectKey }).IsUnique().HasFilter("[DirectKey] IS NOT NULL").HasDatabaseName("UX_MessageThread_Direct");
    }
}

public sealed class MessageThreadMemberConfiguration : IEntityTypeConfiguration<MessageThreadMember>
{
    public void Configure(EntityTypeBuilder<MessageThreadMember> b)
    {
        b.ToTable("MessageThreadMember");
        b.HasKey(m => m.MessageThreadMemberId);
        b.HasIndex(m => new { m.MessageThreadId, m.UserId }).IsUnique().HasDatabaseName("UX_MessageThreadMember");
        b.HasIndex(m => new { m.TenantId, m.UserId, m.LeftAtUtc }).HasDatabaseName("IX_MessageThreadMember_User");
    }
}

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.ToTable("Message");
        b.HasKey(m => m.MessageId);
        b.Property(m => m.Body).HasMaxLength(2000).IsRequired();
        b.HasIndex(m => new { m.MessageThreadId, m.MessageId }).HasDatabaseName("IX_Message_Thread");
    }
}

public sealed class UserPresenceConfiguration : IEntityTypeConfiguration<UserPresence>
{
    public void Configure(EntityTypeBuilder<UserPresence> b)
    {
        b.ToTable("UserPresence");
        b.HasKey(p => p.UserId);
        b.Property(p => p.UserId).ValueGeneratedNever();
    }
}
