using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Sales;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 57 (P4, S1) — órdenes de venta. Mapeo 1:1 con la sección «2026-10-15 (k)» de Diseño/logistica-db-update.sql.

public sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> b)
    {
        b.ToTable("SalesOrder");
        b.HasKey(o => o.SalesOrderId);
        b.Property(o => o.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(o => o.Number).HasMaxLength(40).IsRequired();
        b.Property(o => o.RequestedDate).HasColumnType("date");
        b.Property(o => o.RequiredDate).HasColumnType("date");
        b.Property(o => o.Notes).HasMaxLength(500);
        b.Property(o => o.Subtotal).HasColumnType("decimal(18,4)");
        b.Property(o => o.TaxAmount).HasColumnType("decimal(18,4)");
        b.Property(o => o.RowVersion).IsRowVersion();
        b.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.SalesOrderId);
        b.HasIndex(o => o.PublicId).IsUnique().HasDatabaseName("UQ_SalesOrder_PublicId");
        b.HasIndex(o => new { o.TenantId, o.Number }).IsUnique().HasDatabaseName("UQ_SalesOrder_Number");
        b.HasIndex(o => new { o.TenantId, o.ClientId }).HasDatabaseName("IX_SalesOrder_Client");
        b.HasIndex(o => new { o.TenantId, o.StatusCodeId }).HasDatabaseName("IX_SalesOrder_Status");
    }
}

public sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> b)
    {
        b.ToTable("SalesOrderLine");
        b.HasKey(l => l.SalesOrderLineId);
        foreach (var p in new[] { nameof(SalesOrderLine.QtyOrdered), nameof(SalesOrderLine.QtyReserved), nameof(SalesOrderLine.QtyBackordered), nameof(SalesOrderLine.QtyShipped),
                     nameof(SalesOrderLine.QtyInvoiced), nameof(SalesOrderLine.QtyReturned), nameof(SalesOrderLine.QtyCredited) })
            b.Property(p).HasColumnType("decimal(14,3)");
        b.Property(l => l.UnitPrice).HasColumnType("decimal(18,4)");
        b.Property(l => l.TaxRatePct).HasColumnType("decimal(9,4)");
        b.Property(l => l.TaxAmount).HasColumnType("decimal(18,4)");
        b.HasIndex(l => new { l.SalesOrderId, l.LineNo }).IsUnique().HasDatabaseName("UX_SalesOrderLine_No");
        b.HasIndex(l => new { l.TenantId, l.ProductId }).HasDatabaseName("IX_SalesOrderLine_Product");
    }
}

public sealed class SalesOrderReservationConfiguration : IEntityTypeConfiguration<SalesOrderReservation>
{
    public void Configure(EntityTypeBuilder<SalesOrderReservation> b)
    {
        b.ToTable("SalesOrderReservation");
        b.HasKey(r => r.SalesOrderReservationId);
        b.Property(r => r.Quantity).HasColumnType("decimal(14,3)");
        b.HasIndex(r => r.SalesOrderLineId).HasDatabaseName("IX_SalesOrderReservation_Line");
    }
}
