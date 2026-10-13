using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Printing;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 43 (F-G1) — Impresoras y plantillas de label. Mapeo 1:1 con la sección «2026-10-12 (d)» de Diseño/logistica-db-update.sql
// (dbo.Printer, dbo.LabelTemplate). Los índices únicos filtrados (nombre entre las activas, una predeterminada por uso o por compañía)
// y los CHECK viven en SQL como guardas de última línea; aquí se espejan los índices con el mismo nombre y filtro.

public sealed class PrinterConfiguration : IEntityTypeConfiguration<Printer>
{
    public void Configure(EntityTypeBuilder<Printer> b)
    {
        b.ToTable("Printer");
        b.HasKey(p => p.PrinterId);
        b.Property(p => p.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(p => p.Name).HasMaxLength(80).IsRequired();
        b.Property(p => p.SystemName).HasMaxLength(200);
        b.Property(p => p.Model).HasMaxLength(80);
        b.Property(p => p.Notes).HasMaxLength(300);
        b.Property(p => p.Purpose).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(p => p.ConnectionType).HasMaxLength(12).IsUnicode(false).IsRequired();
        b.Property(p => p.MediaSize).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(p => p.RowVersion).IsRowVersion();

        b.HasIndex(p => p.PublicId).IsUnique().HasDatabaseName("UQ_Printer_PublicId");
        b.HasIndex(p => new { p.TenantId, p.Name }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_Printer_Name");
        b.HasIndex(p => new { p.TenantId, p.Purpose }).IsUnique().HasFilter("[IsDefault] = 1 AND [IsActive] = 1").HasDatabaseName("UX_Printer_Default");
    }
}

public sealed class LabelTemplateConfiguration : IEntityTypeConfiguration<LabelTemplate>
{
    public void Configure(EntityTypeBuilder<LabelTemplate> b)
    {
        b.ToTable("LabelTemplate");
        b.HasKey(t => t.LabelTemplateId);
        b.Property(t => t.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(t => t.Name).HasMaxLength(80).IsRequired();
        b.Property(t => t.LayoutJson).HasMaxLength(LabelLayout.MaxJsonLength).IsRequired();
        b.Property(t => t.RowVersion).IsRowVersion();

        b.HasIndex(t => t.PublicId).IsUnique().HasDatabaseName("UQ_LabelTemplate_PublicId");
        b.HasIndex(t => new { t.TenantId, t.Name }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_LabelTemplate_Name");
        b.HasIndex(t => t.TenantId).IsUnique().HasFilter("[IsDefault] = 1 AND [IsActive] = 1").HasDatabaseName("UX_LabelTemplate_Default");
    }
}
