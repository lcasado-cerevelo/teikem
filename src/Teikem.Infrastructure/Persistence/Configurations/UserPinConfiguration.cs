using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Entities;

namespace Teikem.Infrastructure.Persistence.Configurations;

/// <summary>Lote 8A — dbo.UserPin (PIN por usuario y compañía para los aparatos de almacén), 1:1 con el SQL de estructura.</summary>
public sealed class UserPinConfiguration : IEntityTypeConfiguration<UserPin>
{
    public void Configure(EntityTypeBuilder<UserPin> b)
    {
        b.ToTable("UserPin");
        b.HasKey(x => x.UserPinId);
        b.Property(x => x.PinHash).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique().HasDatabaseName("UQ_UserPin_User");
    }
}
