using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Billing;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 72 (P5) — facturación. Mapeo 1:1 con las tablas del DDL original (BillingRun, Invoice, InvoiceLine, Payment) más las columnas de la sección «2026-10-16 (r)» de update.sql.

public sealed class BillingRunConfiguration : IEntityTypeConfiguration<BillingRun>
{
    public void Configure(EntityTypeBuilder<BillingRun> b)
    {
        b.ToTable("BillingRun");
        b.HasKey(r => r.BillingRunId);
        b.Property(r => r.PeriodStart).HasColumnType("date");
        b.Property(r => r.PeriodEnd).HasColumnType("date");
        b.Property(r => r.TotalAmount).HasColumnType("decimal(18,4)");
        b.Property(r => r.Note).HasMaxLength(500);
        b.HasIndex(r => new { r.TenantId, r.StatusCodeId }).HasDatabaseName("IX_BillingRun_Status");
    }
}

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("Invoice");
        b.HasKey(i => i.InvoiceId);
        b.Property(i => i.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(i => i.InvoiceNumber).HasMaxLength(40).IsRequired();
        b.Property(i => i.IssueDate).HasColumnType("date");
        b.Property(i => i.DueDate).HasColumnType("date");
        b.Property(i => i.Subtotal).HasColumnType("decimal(18,4)");
        b.Property(i => i.TaxAmount).HasColumnType("decimal(18,4)");
        b.Property(i => i.Notes).HasMaxLength(500);
        b.Property(i => i.RowVersion).IsRowVersion();
        b.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId);
        b.HasIndex(i => new { i.TenantId, i.InvoiceNumber }).IsUnique().HasDatabaseName("UQ_Invoice_Number");
        b.HasIndex(i => i.PublicId).HasDatabaseName("IX_Invoice_PublicId");
        b.HasIndex(i => i.BillingRunId).HasDatabaseName("IX_Invoice_Run");
    }
}

public sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> b)
    {
        b.ToTable("InvoiceLine");
        b.HasKey(l => l.InvoiceLineId);
        b.Property(l => l.Description).HasMaxLength(250).IsRequired();
        b.Property(l => l.Quantity).HasColumnType("decimal(14,3)");
        b.Property(l => l.UnitAmount).HasColumnType("decimal(18,4)");
        b.Property(l => l.OriginalUnitAmount).HasColumnType("decimal(18,4)");
        b.Property(l => l.TaxRatePct).HasColumnType("decimal(9,4)");
        b.Property(l => l.TaxAmount).HasColumnType("decimal(18,4)");
        b.Property(l => l.AdjustmentNote).HasMaxLength(250);
        b.HasIndex(l => l.SalesOrderLineId).HasDatabaseName("IX_InvoiceLine_SalesOrderLine");
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payment");
        b.HasKey(p => p.PaymentId);
        b.Property(p => p.Amount).HasColumnType("decimal(18,4)");
        b.Property(p => p.Reference).HasMaxLength(80);
        b.HasIndex(p => p.InvoiceId).HasDatabaseName("IX_Payment_Invoice");
    }
}
