using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Export;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 77 (P5) — plantillas de exportación a contabilidad. La tabla y el índice los crea la sección «2026-10-16 (u)» de update.sql.
public sealed class ExportTemplateConfiguration : IEntityTypeConfiguration<ExportTemplate>
{
    public void Configure(EntityTypeBuilder<ExportTemplate> b)
    {
        b.ToTable("ExportTemplate");
        b.HasKey(t => t.ExportTemplateId);
        b.Property(t => t.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(t => t.Scope).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        b.Property(t => t.Name).HasMaxLength(ExportRules.MaxNameLength).IsRequired();
        b.Property(t => t.Format).HasColumnType("varchar(3)").HasMaxLength(3).IsRequired();
        b.Property(t => t.Delimiter).HasColumnType("nchar(1)").HasMaxLength(1).IsRequired();
        b.Property(t => t.DateFormat).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        b.Property(t => t.ColumnsJson).IsRequired();
        b.Property(t => t.RowVersion).IsRowVersion();
        b.HasIndex(t => t.PublicId);
        b.HasIndex(t => new { t.TenantId, t.Scope, t.Name }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_ExportTemplate_Name");
    }
}
