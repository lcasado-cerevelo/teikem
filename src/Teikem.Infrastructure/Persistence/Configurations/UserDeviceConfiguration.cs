using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Entities;

namespace Teikem.Infrastructure.Persistence.Configurations;

/// <summary>Lote 8A — dbo.UserDevice (aparato de confianza de la app de almacén), 1:1 con logistica-db-estructura.sql.</summary>
public sealed class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
{
    public void Configure(EntityTypeBuilder<UserDevice> b)
    {
        b.ToTable("UserDevice");
        b.HasKey(x => x.UserDeviceId);
        b.Property(x => x.PublicId).HasDefaultValueSql("NEWID()");
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Model).HasMaxLength(80);
        b.Property(x => x.AppVersion).HasMaxLength(20);
        b.Property(x => x.EnrollCodeHash).HasMaxLength(200);
        b.Property(x => x.SecretHash).HasMaxLength(200);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("UQ_UserDevice_Code");
        b.HasIndex(x => x.PublicId).IsUnique().HasDatabaseName("UQ_UserDevice_PublicId");
    }
}
