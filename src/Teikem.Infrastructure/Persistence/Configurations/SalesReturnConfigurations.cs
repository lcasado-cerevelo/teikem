using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Sales;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 81 (P6) — devoluciones de venta. Las tablas y los índices los crea la sección «2026-10-16 (w)» de update.sql.

public sealed class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    public void Configure(EntityTypeBuilder<SalesReturn> b)
    {
        b.ToTable("SalesReturn");
        b.HasKey(r => r.SalesReturnId);
        b.Property(r => r.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(r => r.Number).HasMaxLength(30).IsRequired();
        b.Property(r => r.Note).HasMaxLength(SalesReturnRules.MaxNoteLength);
        b.Property(r => r.AuthorizationNote).HasMaxLength(SalesReturnRules.MaxNoteLength);
        b.Property(r => r.RowVersion).IsRowVersion();
        b.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.SalesReturnId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => r.PublicId);
        b.HasIndex(r => new { r.TenantId, r.Number }).IsUnique().HasDatabaseName("UX_SalesReturn_Number");
        b.HasIndex(r => new { r.TenantId, r.SalesOrderId }).HasDatabaseName("IX_SalesReturn_Order");
    }
}

public sealed class SalesReturnLineConfiguration : IEntityTypeConfiguration<SalesReturnLine>
{
    public void Configure(EntityTypeBuilder<SalesReturnLine> b)
    {
        b.ToTable("SalesReturnLine");
        b.HasKey(l => l.SalesReturnLineId);
        b.Property(l => l.QtyRequested).HasColumnType("decimal(14,3)");
        b.Property(l => l.QtyReceived).HasColumnType("decimal(14,3)");
        b.Property(l => l.QtyDamaged).HasColumnType("decimal(14,3)");
        b.Property(l => l.QtyCredited).HasColumnType("decimal(14,3)");
        b.HasIndex(l => l.SalesOrderLineId).HasDatabaseName("IX_SalesReturnLine_OrderLine");
    }
}
