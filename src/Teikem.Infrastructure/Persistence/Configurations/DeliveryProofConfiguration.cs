using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Orders;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 118 — comprobante de entrega (registrada desde la web).
public sealed class DeliveryProofConfiguration : IEntityTypeConfiguration<DeliveryProof>
{
    public void Configure(EntityTypeBuilder<DeliveryProof> b)
    {
        b.ToTable("DeliveryProof");
        b.HasKey(p => p.DeliveryProofId);
        b.Property(p => p.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(p => p.RecipientName).HasMaxLength(DeliveryProofRules.MaxRecipientLength);
        b.Property(p => p.Notes).HasMaxLength(DeliveryProofRules.MaxNotesLength);
        b.Property(p => p.Source).HasMaxLength(20);
        b.Property(p => p.SignatureKind).HasMaxLength(20);
        b.Property(p => p.GeoStatus).HasMaxLength(12);
        b.Property(p => p.GeoAccuracy).HasMaxLength(8);
        b.Property(p => p.Latitude).HasColumnType("decimal(9,6)");
        b.Property(p => p.Longitude).HasColumnType("decimal(9,6)");
        b.HasIndex(p => p.PublicId).IsUnique().HasDatabaseName("IX_DeliveryProof_PublicId");
        b.HasIndex(p => p.TransportOrderId).HasDatabaseName("UX_DeliveryProof_Order");
    }
}
