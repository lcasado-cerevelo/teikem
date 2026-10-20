using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.ExitNotices;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 143 — aviso de salida. Mapeo 1:1 con la sección «2026-10-20 — Aviso de salida» de Diseño/logistica-db-update.sql.

public sealed class ExitNoticeConfiguration : IEntityTypeConfiguration<ExitNotice>
{
    public void Configure(EntityTypeBuilder<ExitNotice> b)
    {
        b.ToTable("ExitNotice");
        b.HasKey(o => o.ExitNoticeId);
        b.Property(o => o.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(o => o.Number).HasMaxLength(40).IsRequired();
        b.Property(o => o.Reference).HasMaxLength(80);
        b.Property(o => o.Destination).HasMaxLength(200);
        b.Property(o => o.RequestedDate).HasColumnType("date");
        b.Property(o => o.RequiredDate).HasColumnType("date");
        b.Property(o => o.Notes).HasMaxLength(500);
        b.Property(o => o.RowVersion).IsRowVersion();
        b.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.ExitNoticeId);
        b.HasIndex(o => o.PublicId).IsUnique().HasDatabaseName("UQ_ExitNotice_PublicId");
        b.HasIndex(o => new { o.TenantId, o.Number }).IsUnique().HasDatabaseName("UQ_ExitNotice_Number");
        b.HasIndex(o => new { o.TenantId, o.ClientId }).HasDatabaseName("IX_ExitNotice_Client");
        b.HasIndex(o => new { o.TenantId, o.StatusCodeId }).HasDatabaseName("IX_ExitNotice_Status");
    }
}

public sealed class ExitNoticeLineConfiguration : IEntityTypeConfiguration<ExitNoticeLine>
{
    public void Configure(EntityTypeBuilder<ExitNoticeLine> b)
    {
        b.ToTable("ExitNoticeLine");
        b.HasKey(l => l.ExitNoticeLineId);
        foreach (var p in new[] { nameof(ExitNoticeLine.QtyOrdered), nameof(ExitNoticeLine.QtyReserved), nameof(ExitNoticeLine.QtyBackordered), nameof(ExitNoticeLine.QtyShipped) })
            b.Property(p).HasColumnType("decimal(14,3)");
        b.HasIndex(l => new { l.ExitNoticeId, l.LineNo }).IsUnique().HasDatabaseName("UX_ExitNoticeLine_No");
        b.HasIndex(l => new { l.TenantId, l.ProductId }).HasDatabaseName("IX_ExitNoticeLine_Product");
    }
}

public sealed class ExitNoticeReservationConfiguration : IEntityTypeConfiguration<ExitNoticeReservation>
{
    public void Configure(EntityTypeBuilder<ExitNoticeReservation> b)
    {
        b.ToTable("ExitNoticeReservation");
        b.HasKey(r => r.ExitNoticeReservationId);
        b.Property(r => r.Quantity).HasColumnType("decimal(14,3)");
        b.HasIndex(r => r.ExitNoticeLineId).HasDatabaseName("IX_ExitNoticeReservation_Line");
    }
}
