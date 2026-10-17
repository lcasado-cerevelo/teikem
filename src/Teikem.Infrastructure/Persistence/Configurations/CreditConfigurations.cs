using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Billing;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 79 (P6) — notas de crédito. Las tablas y los índices los crea la sección «2026-10-16 (v)» de update.sql.

public sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> b)
    {
        b.ToTable("CreditNote");
        b.HasKey(n => n.CreditNoteId);
        b.Property(n => n.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(n => n.CreditNumber).HasMaxLength(30).IsRequired();
        b.Property(n => n.ExternalInvoiceRef).HasMaxLength(CreditNoteRules.MaxRefLength);
        b.Property(n => n.Subtotal).HasColumnType("decimal(18,4)");
        b.Property(n => n.TaxAmount).HasColumnType("decimal(18,4)");
        b.Property(n => n.Note).HasMaxLength(CreditNoteRules.MaxNoteLength);
        b.Property(n => n.RowVersion).IsRowVersion();
        b.HasMany(n => n.Lines).WithOne().HasForeignKey(l => l.CreditNoteId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(n => n.PublicId);
        b.HasIndex(n => new { n.TenantId, n.CreditNumber }).IsUnique().HasDatabaseName("UX_CreditNote_Number");
        b.HasIndex(n => new { n.TenantId, n.ClientId, n.StatusCodeId }).HasDatabaseName("IX_CreditNote_Client");
    }
}

public sealed class CreditNoteLineConfiguration : IEntityTypeConfiguration<CreditNoteLine>
{
    public void Configure(EntityTypeBuilder<CreditNoteLine> b)
    {
        b.ToTable("CreditNoteLine");
        b.HasKey(l => l.CreditNoteLineId);
        b.Property(l => l.Description).HasMaxLength(CreditNoteRules.MaxDescriptionLength).IsRequired();
        b.Property(l => l.Quantity).HasColumnType("decimal(14,3)");
        b.Property(l => l.UnitAmount).HasColumnType("decimal(18,4)");
        b.Property(l => l.TaxRatePct).HasColumnType("decimal(9,4)");
        b.Property(l => l.TaxAmount).HasColumnType("decimal(18,4)");
        b.HasIndex(l => l.InvoiceLineId).HasDatabaseName("IX_CreditNoteLine_InvoiceLine");
    }
}

public sealed class CreditApplicationConfiguration : IEntityTypeConfiguration<CreditApplication>
{
    public void Configure(EntityTypeBuilder<CreditApplication> b)
    {
        b.ToTable("CreditApplication");
        b.HasKey(a => a.CreditApplicationId);
        b.Property(a => a.Amount).HasColumnType("decimal(18,4)");
        b.HasIndex(a => a.InvoiceId).HasDatabaseName("IX_CreditApplication_Invoice");
        b.HasIndex(a => a.CreditNoteId).HasDatabaseName("IX_CreditApplication_Note");
    }
}
