using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Files;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 127 — el almacén de archivos (referencias; el contenido vive cifrado en IFileStore).
public sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> b)
    {
        b.ToTable("StoredFile");
        b.HasKey(f => f.StoredFileId);
        b.Property(f => f.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(f => f.Kind).HasMaxLength(30);
        b.Property(f => f.FileName).HasMaxLength(StoredFileRules.MaxFileNameLength);
        b.Property(f => f.ContentType).HasMaxLength(100);
        b.Property(f => f.Sha256).HasMaxLength(64);
        b.Property(f => f.StorageKey).HasMaxLength(300);
        b.HasOne(f => f.OwnerEntity).WithMany().HasForeignKey(f => f.OwnerEntityLookupId);
        b.HasIndex(f => f.PublicId).IsUnique().HasDatabaseName("UX_StoredFile_PublicId");
        b.HasIndex(f => new { f.OwnerEntityLookupId, f.OwnerId }).HasDatabaseName("IX_StoredFile_Owner");
    }
}
