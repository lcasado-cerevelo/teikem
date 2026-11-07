using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Billing;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 224 (P6 del pedido de vendedores) — pagos de consignación. Mapeo 1:1 con dbo.ConsignmentPayment (sección «2026-11-07 — Lote 224» de logistica-db-update.sql).

public sealed class ConsignmentPaymentConfiguration : IEntityTypeConfiguration<ConsignmentPayment>
{
    public void Configure(EntityTypeBuilder<ConsignmentPayment> b)
    {
        b.ToTable("ConsignmentPayment");
        b.HasKey(p => p.ConsignmentPaymentId);
        b.Property(p => p.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(p => p.Amount).HasColumnType("decimal(18,4)");
        b.Property(p => p.Reference).HasMaxLength(80);
        b.Property(p => p.VoidReason).HasMaxLength(250);
        b.HasIndex(p => p.PublicId).IsUnique().HasDatabaseName("UX_ConsignmentPayment_PublicId");
        b.HasIndex(p => new { p.TenantId, p.InvoiceId }).HasDatabaseName("IX_ConsignmentPayment_Invoice");
        b.HasIndex(p => new { p.TenantId, p.PaidDate }).HasDatabaseName("IX_ConsignmentPayment_PaidDate");
        b.HasOne<Invoice>().WithMany().HasForeignKey(p => p.InvoiceId);
        b.HasOne<Payment>().WithMany().HasForeignKey(p => p.PaymentId);
        b.HasOne<Teikem.Domain.Catalogs.LookupCode>().WithMany().HasForeignKey(p => p.PaymentMethodLookupId);
    }
}
