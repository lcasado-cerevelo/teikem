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
        b.HasIndex(t => new { t.TenantId, t.FollowsRoleId }).IsUnique().HasFilter("[FollowsRoleId] IS NOT NULL").HasDatabaseName("UX_MessageThread_Role");
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
        b.Property(m => m.RefEntity).HasMaxLength(20).IsUnicode(false);
        b.HasIndex(m => new { m.TenantId, m.RefEntity, m.RefPublicId }).HasFilter("[RefPublicId] IS NOT NULL").HasDatabaseName("IX_Message_Ref");
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

// Lote 104 — mensajes del portal con el equipo de servicio al cliente. Mapeo 1:1 con la sección «2026-10-17 (am)» de Diseño/logistica-db-update.sql.

public sealed class ClientConversationConfiguration : IEntityTypeConfiguration<ClientConversation>
{
    public void Configure(EntityTypeBuilder<ClientConversation> b)
    {
        b.ToTable("ClientConversation");
        b.HasKey(c => c.ClientConversationId);
        b.Property(c => c.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(c => c.StartedBy).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(c => c.LastMessagePreview).HasMaxLength(200);
        b.Property(c => c.LastSenderKind).HasMaxLength(10).IsUnicode(false);
        b.HasIndex(c => c.PublicId).IsUnique().HasDatabaseName("UQ_ClientConversation_PublicId");
        b.HasIndex(c => new { c.TenantId, c.ClientId, c.PortalUserId }).IsUnique().HasDatabaseName("UX_ClientConversation_Person");
        b.HasIndex(c => new { c.TenantId, c.LastMessageAtUtc }).HasDatabaseName("IX_ClientConversation_Last");
    }
}

public sealed class ClientMessageConfiguration : IEntityTypeConfiguration<ClientMessage>
{
    public void Configure(EntityTypeBuilder<ClientMessage> b)
    {
        b.ToTable("ClientMessage");
        b.HasKey(m => m.ClientMessageId);
        b.Property(m => m.SenderKind).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(m => m.Body).HasMaxLength(2000).IsRequired();
        b.HasIndex(m => new { m.ClientConversationId, m.ClientMessageId }).HasDatabaseName("IX_ClientMessage_Conversation");
        b.HasIndex(m => new { m.SenderUserId, m.CreatedAtUtc }).HasDatabaseName("IX_ClientMessage_Sender");
    }
}

public sealed class ClientServiceTeamMemberConfiguration : IEntityTypeConfiguration<ClientServiceTeamMember>
{
    public void Configure(EntityTypeBuilder<ClientServiceTeamMember> b)
    {
        b.ToTable("ClientServiceTeamMember");
        b.HasKey(m => m.ClientServiceTeamMemberId);
        b.HasIndex(m => new { m.TenantId, m.ClientId, m.UserId }).IsUnique().HasDatabaseName("UX_ClientServiceTeamMember");
    }
}

// Lote 117b — mensajes del consignatario con su cliente. Mapeo 1:1 con la sección «2026-10-18 (az)» de Diseño/logistica-db-update.sql.

public sealed class ConsigneeConversationConfiguration : IEntityTypeConfiguration<ConsigneeConversation>
{
    public void Configure(EntityTypeBuilder<ConsigneeConversation> b)
    {
        b.ToTable("ConsigneeConversation");
        b.HasKey(c => c.ConsigneeConversationId);
        b.Property(c => c.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(c => c.LastMessagePreview).HasMaxLength(200);
        b.Property(c => c.LastSenderKind).HasMaxLength(10).IsUnicode(false);
        b.HasIndex(c => c.PublicId).IsUnique().HasDatabaseName("UQ_ConsigneeConversation_PublicId");
        b.HasIndex(c => new { c.TenantId, c.ClientId, c.LocationId, c.ConsigneeUserId }).IsUnique().HasDatabaseName("UX_ConsigneeConversation_Person");
        b.HasIndex(c => new { c.TenantId, c.ClientId, c.LastMessageAtUtc }).HasDatabaseName("IX_ConsigneeConversation_Last");
    }
}

public sealed class ConsigneeMessageConfiguration : IEntityTypeConfiguration<ConsigneeMessage>
{
    public void Configure(EntityTypeBuilder<ConsigneeMessage> b)
    {
        b.ToTable("ConsigneeMessage");
        b.HasKey(m => m.ConsigneeMessageId);
        b.Property(m => m.SenderKind).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(m => m.Body).HasMaxLength(2000).IsRequired();
        b.HasIndex(m => new { m.ConsigneeConversationId, m.ConsigneeMessageId }).HasDatabaseName("IX_ConsigneeMessage_Conversation");
        b.HasIndex(m => new { m.SenderUserId, m.CreatedAtUtc }).HasDatabaseName("IX_ConsigneeMessage_Sender");
    }
}

public sealed class EntityResponsibleConfiguration : IEntityTypeConfiguration<EntityResponsible>
{
    public void Configure(EntityTypeBuilder<EntityResponsible> b)
    {
        b.ToTable("EntityResponsible");
        b.HasKey(r => r.EntityResponsibleId);
        b.HasIndex(r => new { r.TenantId, r.EntityLookupId, r.EntityId, r.UserId }).IsUnique().HasDatabaseName("UX_EntityResponsible");
    }
}

public sealed class MessageAttachmentConfiguration : IEntityTypeConfiguration<MessageAttachment>
{
    public void Configure(EntityTypeBuilder<MessageAttachment> b)
    {
        b.ToTable("MessageAttachment");
        b.HasKey(a => a.MessageAttachmentId);
        b.Property(a => a.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(a => a.ContentType).HasMaxLength(100).IsUnicode(false).IsRequired();
        b.Property(a => a.Sha256).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(a => a.StorageKey).HasMaxLength(300);
        b.Property(a => a.ThumbStorageKey).HasMaxLength(300);
        b.Property(a => a.State).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.HasIndex(a => a.PublicId).IsUnique().HasDatabaseName("UX_MessageAttachment_PublicId");
        b.HasIndex(a => a.MessageId).HasDatabaseName("IX_MessageAttachment_Message");
        b.HasIndex(a => new { a.TenantId, a.State, a.CreatedAtUtc }).HasDatabaseName("IX_MessageAttachment_Purge");
    }
}
