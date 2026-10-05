using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Wms;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 27 (Rentas R1) — mapeo 1:1 de la capa 16C reescrita de Diseño/logistica-db-estructura.sql. Sin migraciones EF: el esquema lo
// crea el script. RentalCharge (cobro por período, futuro) queda sin mapear a propósito.

public sealed class RentalConfiguration : IEntityTypeConfiguration<Rental>
{
    public void Configure(EntityTypeBuilder<Rental> b)
    {
        b.ToTable("Rental");
        b.HasKey(r => r.RentalId);
        b.Property(r => r.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(r => r.Number).HasMaxLength(40).IsRequired();
        b.Property(r => r.StartDate).HasColumnType("date");
        b.Property(r => r.PickupDate).HasColumnType("date");
        b.Property(r => r.OriginalPickupDate).HasColumnType("date");
        b.Property(r => r.ContractNumber).HasMaxLength(80);
        b.Property(r => r.ContractSignedOn).HasColumnType("date");
        b.Property(r => r.EstimatedDeliveryCost).HasColumnType("decimal(18,4)");
        b.Property(r => r.Notes).HasMaxLength(1000);
        b.Property(r => r.RowVersion).IsRowVersion();

        b.HasIndex(r => new { r.TenantId, r.Number }).IsUnique().HasDatabaseName("UQ_Rental_Number");
        b.HasIndex(r => new { r.TenantId, r.StatusCodeId, r.PickupDate }).HasDatabaseName("IX_Rental_StatusPickup");

        b.HasOne(r => r.Status).WithMany().HasForeignKey(r => r.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Teikem.Domain.Clients.Client>().WithMany().HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Teikem.Domain.Clients.Location>().WithMany().HasForeignKey(r => r.LocationId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(r => r.WarehouseId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class RentalLineConfiguration : IEntityTypeConfiguration<RentalLine>
{
    public void Configure(EntityTypeBuilder<RentalLine> b)
    {
        b.ToTable("RentalLine");
        b.HasKey(l => l.RentalLineId);
        b.HasIndex(l => l.RentalId).HasDatabaseName("IX_RentalLine_Rental");
        b.HasOne<Rental>().WithMany().HasForeignKey(l => l.RentalId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class RentalLineRateConfiguration : IEntityTypeConfiguration<RentalLineRate>
{
    public void Configure(EntityTypeBuilder<RentalLineRate> b)
    {
        b.ToTable("RentalLineRate");
        b.HasKey(r => r.RentalLineRateId);
        b.Property(r => r.RateAmount).HasColumnType("decimal(18,4)");
        b.Property(r => r.EffectiveFrom).HasColumnType("date");
        b.Property(r => r.EffectiveTo).HasColumnType("date");
        b.HasIndex(r => new { r.RentalLineId, r.EffectiveFrom }).HasDatabaseName("IX_RentalLineRate_Line");
        b.HasOne<RentalLine>().WithMany().HasForeignKey(r => r.RentalLineId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class RentalExtensionConfiguration : IEntityTypeConfiguration<RentalExtension>
{
    public void Configure(EntityTypeBuilder<RentalExtension> b)
    {
        b.ToTable("RentalExtension");
        b.HasKey(e => e.RentalExtensionId);
        b.Property(e => e.PreviousPickupDate).HasColumnType("date");
        b.Property(e => e.NewPickupDate).HasColumnType("date");
        b.Property(e => e.Reason).HasMaxLength(300).IsRequired();
        b.HasIndex(e => e.RentalId).HasDatabaseName("IX_RentalExtension_Rental");
        b.HasOne<Rental>().WithMany().HasForeignKey(e => e.RentalId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class RentalReturnConfiguration : IEntityTypeConfiguration<RentalReturn>
{
    public void Configure(EntityTypeBuilder<RentalReturn> b)
    {
        b.ToTable("RentalReturn");
        b.HasKey(r => r.RentalReturnId);
        b.Property(r => r.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(r => r.Number).HasMaxLength(40).IsRequired();
        b.Property(r => r.ReturnedOn).HasColumnType("date");
        b.Property(r => r.Notes).HasMaxLength(1000);
        b.Property(r => r.EstimatedPickupCost).HasColumnType("decimal(18,4)");
        b.HasIndex(r => new { r.TenantId, r.Number }).IsUnique().HasDatabaseName("UQ_RentalReturn_Number");
        b.HasIndex(r => r.RentalId).HasDatabaseName("IX_RentalReturn_Rental");
        b.HasOne<Rental>().WithMany().HasForeignKey(r => r.RentalId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class RentalReturnLineConfiguration : IEntityTypeConfiguration<RentalReturnLine>
{
    public void Configure(EntityTypeBuilder<RentalReturnLine> b)
    {
        b.ToTable("RentalReturnLine");
        b.HasKey(l => l.RentalReturnLineId);
        b.Property(l => l.Notes).HasMaxLength(500);
        b.HasIndex(l => l.RentalLineId).IsUnique().HasDatabaseName("UQ_RentalReturnLine_Line");
        b.HasOne<RentalReturn>().WithMany().HasForeignKey(l => l.RentalReturnId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<RentalLine>().WithMany().HasForeignKey(l => l.RentalLineId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class RentalProcessConfiguration : IEntityTypeConfiguration<RentalProcess>
{
    public void Configure(EntityTypeBuilder<RentalProcess> b)
    {
        b.ToTable("RentalProcess");
        b.HasKey(p => p.RentalProcessId);
        b.Property(p => p.Notes).HasMaxLength(1000);
        b.Property(p => p.RowVersion).IsRowVersion();
        b.HasIndex(p => new { p.TenantId, p.StatusCodeId }).HasDatabaseName("IX_RentalProcess_Status");
        b.HasOne(p => p.Status).WithMany().HasForeignKey(p => p.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
    }
}
