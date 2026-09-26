using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Fleet;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 4 — Pago a choferes (maestro del módulo 11A; el chofer es el agregado raíz, sin DriverRateAgreement) y viaje pagado
// (DriverTrip). Mapeo 1:1 con las tablas nuevas de CAPA 10 y CAPA 13. Las FKs compuestas (Id, TenantId) del SQL no se
// mapean en EF (relaciones por columna simple). Índices únicos filtrados espejo del SQL, con el mismo nombre y filtro.

public sealed class DriverPayPolicyConfiguration : IEntityTypeConfiguration<DriverPayPolicy>
{
    public void Configure(EntityTypeBuilder<DriverPayPolicy> b)
    {
        b.ToTable("DriverPayPolicy");
        // Una fila por compañía: la PK es el TenantId (nunca generado por la BD).
        b.HasKey(p => p.TenantId);
        b.Property(p => p.TenantId).ValueGeneratedNever();
        b.HasOne(p => p.PayoutFormula).WithMany().HasForeignKey(p => p.PayoutFormulaLookupId);
    }
}

public sealed class DriverDeliveryRateConfiguration : IEntityTypeConfiguration<DriverDeliveryRate>
{
    public void Configure(EntityTypeBuilder<DriverDeliveryRate> b)
    {
        b.ToTable("DriverDeliveryRate");
        b.HasKey(r => r.DriverDeliveryRateId);
        b.Property(r => r.Rate).HasColumnType("decimal(18,4)");
        b.HasIndex(r => new { r.DriverId, r.ServiceTypeLookupId, r.PackageTypeLookupId }).IsUnique()
            .HasFilter("[EffectiveTo] IS NULL AND [IsActive] = 1").HasDatabaseName("UQ_DriverDeliveryRate_Open");
        b.HasIndex(r => new { r.TenantId, r.DriverId }).HasDatabaseName("IX_DriverDeliveryRate_Driver");
        b.HasOne(r => r.Driver).WithMany().HasForeignKey(r => r.DriverId);
        b.HasOne(r => r.ServiceType).WithMany().HasForeignKey(r => r.ServiceTypeLookupId);
        b.HasOne(r => r.PackageType).WithMany().HasForeignKey(r => r.PackageTypeLookupId);
    }
}

public sealed class DriverAttemptRateConfiguration : IEntityTypeConfiguration<DriverAttemptRate>
{
    public void Configure(EntityTypeBuilder<DriverAttemptRate> b)
    {
        b.ToTable("DriverAttemptRate");
        b.HasKey(r => r.DriverAttemptRateId);
        b.Property(r => r.Rate).HasColumnType("decimal(18,4)");
        b.HasIndex(r => new { r.DriverId, r.AttemptNumber }).IsUnique()
            .HasFilter("[EffectiveTo] IS NULL AND [IsActive] = 1").HasDatabaseName("UQ_DriverAttemptRate_Open");
        b.HasIndex(r => new { r.TenantId, r.DriverId }).HasDatabaseName("IX_DriverAttemptRate_Driver");
        b.HasOne(r => r.Driver).WithMany().HasForeignKey(r => r.DriverId);
    }
}

public sealed class DriverTripRateConfiguration : IEntityTypeConfiguration<DriverTripRate>
{
    public void Configure(EntityTypeBuilder<DriverTripRate> b)
    {
        b.ToTable("DriverTripRate");
        b.HasKey(r => r.DriverTripRateId);
        b.Property(r => r.Rate).HasColumnType("decimal(18,4)");
        b.HasIndex(r => new { r.DriverId, r.SpecialServiceTypeId }).IsUnique()
            .HasFilter("[EffectiveTo] IS NULL AND [IsActive] = 1").HasDatabaseName("UQ_DriverTripRate_Open");
        b.HasIndex(r => new { r.TenantId, r.DriverId }).HasDatabaseName("IX_DriverTripRate_Driver");
        b.HasOne(r => r.Driver).WithMany().HasForeignKey(r => r.DriverId);
        b.HasOne(r => r.TripType).WithMany().HasForeignKey(r => r.SpecialServiceTypeId);
    }
}

public sealed class DriverTripConfiguration : IEntityTypeConfiguration<DriverTrip>
{
    public void Configure(EntityTypeBuilder<DriverTrip> b)
    {
        b.ToTable("DriverTrip");
        b.HasKey(t => t.DriverTripId);
        b.Property(t => t.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(t => t.Amount).HasColumnType("decimal(18,4)");
        b.Property(t => t.Notes).HasMaxLength(500);
        b.Property(t => t.RowVersion).IsRowVersion();

        // Un solo viaje vigente por orden (CANCELLED implica IsActive = 0, DriverTripStatusEffect).
        b.HasIndex(t => t.TransportOrderId).IsUnique()
            .HasFilter("[TransportOrderId] IS NOT NULL AND [IsActive] = 1").HasDatabaseName("UX_DriverTrip_Order");
        b.HasIndex(t => new { t.TenantId, t.DriverId, t.TripDate }).HasDatabaseName("IX_DriverTrip_Driver_Date");
        b.HasIndex(t => t.PublicId);

        b.HasOne(t => t.Driver).WithMany().HasForeignKey(t => t.DriverId);
        b.HasOne(t => t.TripType).WithMany().HasForeignKey(t => t.SpecialServiceTypeId);
        b.HasOne(t => t.Rate).WithMany().HasForeignKey(t => t.DriverTripRateId);
        b.HasOne(t => t.Order).WithMany().HasForeignKey(t => t.TransportOrderId);
        b.HasOne(t => t.Status).WithMany().HasForeignKey(t => t.StatusCodeId);
    }
}
