using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Fleet;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 4 — Flota, choferes y mantenimiento. Mapeo 1:1 con Diseño/logistica-db-estructura.sql (CAPA 10 Vehicle/Driver,
// CAPA 12B DispatchZone/DriverZone, CAPA 13 detalle de flota y mantenimiento, CAPA 16 DriverDevice). Los índices únicos
// filtrados y los CHECKs viven en SQL (guardas de última línea); aquí se espejan con el mismo nombre y filtro para que el
// modelo EF coincida con el esquema. Las FKs compuestas (Id, TenantId) del SQL NO se mapean: EF sigue las relaciones por
// columna simple y nunca genera esquema. HomeWarehouseId se mapea sin navegación (Warehouse no está mapeado).

public sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> b)
    {
        b.ToTable("Vehicle");
        b.HasKey(v => v.VehicleId);
        b.Property(v => v.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(v => v.Code).HasMaxLength(30).IsRequired();
        b.Property(v => v.PlateNumber).HasMaxLength(20);
        b.Property(v => v.MaxWeightKg).HasColumnType("decimal(12,3)");
        b.Property(v => v.MaxVolumeM3).HasColumnType("decimal(12,4)");
        b.Property(v => v.Make).HasMaxLength(60);
        b.Property(v => v.Model).HasMaxLength(60);
        b.Property(v => v.Vin).HasMaxLength(40);
        b.Property(v => v.CurrentOdometerKm).HasColumnType("decimal(12,1)");
        b.Property(v => v.RowVersion).IsRowVersion();

        // Código irrepetible por compañía, aunque el otro esté dado de baja (sin filtro).
        b.HasIndex(v => new { v.TenantId, v.Code }).IsUnique().HasDatabaseName("UQ_Vehicle_Code");
        b.HasIndex(v => v.PublicId);

        b.HasOne(v => v.VehicleType).WithMany().HasForeignKey(v => v.VehicleTypeLookupId);
        b.HasOne(v => v.Ownership).WithMany().HasForeignKey(v => v.OwnershipLookupId);
        b.HasOne(v => v.FuelType).WithMany().HasForeignKey(v => v.FuelTypeLookupId);
        b.HasOne(v => v.Status).WithMany().HasForeignKey(v => v.StatusCodeId);
        b.HasMany(v => v.Documents).WithOne(d => d.Vehicle).HasForeignKey(d => d.VehicleId);
    }
}

public sealed class VehicleDocumentConfiguration : IEntityTypeConfiguration<VehicleDocument>
{
    public void Configure(EntityTypeBuilder<VehicleDocument> b)
    {
        b.ToTable("VehicleDocument");
        b.HasKey(d => d.VehicleDocumentId);
        b.Property(d => d.DocNumber).HasMaxLength(80);
        b.Property(d => d.FileName).HasMaxLength(255);
        b.Property(d => d.StoragePath).HasMaxLength(500);
        b.HasIndex(d => d.ExpiryDate).HasFilter("[IsActive] = 1").HasDatabaseName("IX_VehicleDocument_Expiry");
        // Grupo de 'vigente por tipo'
        b.HasIndex(d => new { d.VehicleId, d.DocTypeLookupId }).HasDatabaseName("IX_VehicleDocument_Vehicle");
        b.HasOne(d => d.DocType).WithMany().HasForeignKey(d => d.DocTypeLookupId);
    }
}

public sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> b)
    {
        b.ToTable("Driver");
        b.HasKey(d => d.DriverId);
        b.Property(d => d.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(d => d.EmployeeCode).HasMaxLength(30).IsRequired();
        b.Property(d => d.FullName).HasMaxLength(150).IsRequired();
        b.Property(d => d.RowVersion).IsRowVersion();

        // 'Código' del chofer: irrepetible por compañía, aunque el otro esté dado de baja (sin filtro).
        b.HasIndex(d => new { d.TenantId, d.EmployeeCode }).IsUnique().HasDatabaseName("UQ_Driver_EmployeeCode");
        // Un usuario se vincula a un solo chofer por compañía.
        b.HasIndex(d => new { d.TenantId, d.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL").HasDatabaseName("UX_Driver_User");
        b.HasIndex(d => d.PublicId);

        b.HasOne(d => d.Status).WithMany().HasForeignKey(d => d.StatusCodeId);
        b.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId);
        b.HasMany(d => d.Licenses).WithOne(l => l.Driver).HasForeignKey(l => l.DriverId);
        b.HasMany(d => d.Certifications).WithOne(c => c.Driver).HasForeignKey(c => c.DriverId);
        b.HasMany(d => d.Zones).WithOne(z => z.Driver).HasForeignKey(z => z.DriverId);
        b.HasMany(d => d.Devices).WithOne(x => x.Driver).HasForeignKey(x => x.DriverId);
    }
}

public sealed class DriverLicenseConfiguration : IEntityTypeConfiguration<DriverLicense>
{
    public void Configure(EntityTypeBuilder<DriverLicense> b)
    {
        b.ToTable("DriverLicense");
        b.HasKey(l => l.DriverLicenseId);
        b.Property(l => l.LicenseNumber).HasMaxLength(60).IsRequired();
        b.HasIndex(l => l.ExpiryDate).HasFilter("[IsActive] = 1").HasDatabaseName("IX_DriverLicense_Expiry");
        b.HasIndex(l => new { l.DriverId, l.LicenseClassLookupId }).HasDatabaseName("IX_DriverLicense_Driver");
        b.HasOne(l => l.LicenseClass).WithMany().HasForeignKey(l => l.LicenseClassLookupId);
    }
}

public sealed class DriverCertificationConfiguration : IEntityTypeConfiguration<DriverCertification>
{
    public void Configure(EntityTypeBuilder<DriverCertification> b)
    {
        b.ToTable("DriverCertification");
        b.HasKey(c => c.DriverCertificationId);
        b.Property(c => c.CertNumber).HasMaxLength(60);
        b.HasIndex(c => c.ExpiryDate).HasFilter("[IsActive] = 1").HasDatabaseName("IX_DriverCertification_Expiry");
        b.HasIndex(c => new { c.DriverId, c.CertTypeLookupId }).HasDatabaseName("IX_DriverCertification_Driver");
        b.HasOne(c => c.CertType).WithMany().HasForeignKey(c => c.CertTypeLookupId);
    }
}

public sealed class DriverDeviceConfiguration : IEntityTypeConfiguration<DriverDevice>
{
    public void Configure(EntityTypeBuilder<DriverDevice> b)
    {
        b.ToTable("DriverDevice");
        b.HasKey(x => x.DriverDeviceId);
        b.Property(x => x.PushToken).HasMaxLength(400);
        b.Property(x => x.AppVersion).HasMaxLength(20);
        // Un token de push activo pertenece a un solo dispositivo por compañía.
        b.HasIndex(x => new { x.TenantId, x.PushToken }).IsUnique()
            .HasFilter("[PushToken] IS NOT NULL AND [IsActive] = 1").HasDatabaseName("UX_DriverDevice_Token");
        b.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformLookupId);
    }
}

public sealed class DispatchZoneConfiguration : IEntityTypeConfiguration<DispatchZone>
{
    public void Configure(EntityTypeBuilder<DispatchZone> b)
    {
        b.ToTable("DispatchZone");
        b.HasKey(z => z.DispatchZoneId);
        b.Property(z => z.Code).HasMaxLength(20).IsRequired();
        b.Property(z => z.Name).HasMaxLength(120);
        b.HasIndex(z => new { z.TenantId, z.Code }).IsUnique().HasDatabaseName("UQ_DispatchZone");
    }
}

public sealed class DriverZoneConfiguration : IEntityTypeConfiguration<DriverZone>
{
    public void Configure(EntityTypeBuilder<DriverZone> b)
    {
        b.ToTable("DriverZone");
        b.HasKey(z => new { z.DriverId, z.DispatchZoneId });
        b.HasOne(z => z.Zone).WithMany().HasForeignKey(z => z.DispatchZoneId);
    }
}

public sealed class MaintenanceScheduleConfiguration : IEntityTypeConfiguration<MaintenanceSchedule>
{
    public void Configure(EntityTypeBuilder<MaintenanceSchedule> b)
    {
        b.ToTable("MaintenanceSchedule");
        b.HasKey(s => s.MaintenanceScheduleId);
        b.Property(s => s.Name).HasMaxLength(150).IsRequired();
        b.Property(s => s.IntervalKm).HasColumnType("decimal(12,1)");
        b.Property(s => s.LastServiceKm).HasColumnType("decimal(12,1)");
        b.HasIndex(s => s.VehicleId).HasFilter("[VehicleId] IS NOT NULL").HasDatabaseName("IX_MaintSchedule_Vehicle");
        b.HasOne(s => s.Vehicle).WithMany().HasForeignKey(s => s.VehicleId);
        b.HasOne(s => s.VehicleType).WithMany().HasForeignKey(s => s.VehicleTypeLookupId);
        b.HasOne(s => s.Trigger).WithMany().HasForeignKey(s => s.TriggerLookupId);
    }
}

public sealed class MaintenanceWorkOrderConfiguration : IEntityTypeConfiguration<MaintenanceWorkOrder>
{
    public void Configure(EntityTypeBuilder<MaintenanceWorkOrder> b)
    {
        b.ToTable("MaintenanceWorkOrder");
        b.HasKey(w => w.WorkOrderId);
        b.Property(w => w.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(w => w.Number).HasMaxLength(40).IsRequired();
        b.Property(w => w.OdometerKm).HasColumnType("decimal(12,1)");
        b.Property(w => w.Vendor).HasMaxLength(150);
        b.Property(w => w.LaborCost).HasColumnType("decimal(18,4)");
        b.Property(w => w.PartsCost).HasColumnType("decimal(18,4)");
        // Columna computada PERSISTED de SQL: EF la lee y nunca la escribe.
        b.Property(w => w.TotalCost).HasColumnType("decimal(19,4)")
            .HasComputedColumnSql("ISNULL([LaborCost],0) + ISNULL([PartsCost],0)", stored: true);
        b.Property(w => w.RowVersion).IsRowVersion();

        b.HasIndex(w => new { w.TenantId, w.Number }).IsUnique().HasDatabaseName("UQ_WorkOrder_Number");
        b.HasIndex(w => new { w.VehicleId, w.StatusCodeId }).HasFilter("[IsActive] = 1").HasDatabaseName("IX_WorkOrder_Vehicle");
        b.HasIndex(w => w.MaintenanceScheduleId).HasFilter("[MaintenanceScheduleId] IS NOT NULL").HasDatabaseName("IX_WorkOrder_Schedule");
        b.HasIndex(w => w.PublicId);

        b.HasOne(w => w.Vehicle).WithMany().HasForeignKey(w => w.VehicleId);
        b.HasOne(w => w.Schedule).WithMany().HasForeignKey(w => w.MaintenanceScheduleId);
        b.HasOne(w => w.MaintenanceType).WithMany().HasForeignKey(w => w.MaintenanceTypeLookupId);
        b.HasOne(w => w.Status).WithMany().HasForeignKey(w => w.StatusCodeId);
        b.HasOne(w => w.Currency).WithMany().HasForeignKey(w => w.CurrencyLookupId);
        b.HasMany(w => w.Tasks).WithOne(t => t.WorkOrder).HasForeignKey(t => t.WorkOrderId);
    }
}

public sealed class MaintenanceTaskConfiguration : IEntityTypeConfiguration<MaintenanceTask>
{
    public void Configure(EntityTypeBuilder<MaintenanceTask> b)
    {
        b.ToTable("MaintenanceTask");
        b.HasKey(t => t.MaintenanceTaskId);
        b.Property(t => t.Description).HasMaxLength(250).IsRequired();
        b.Property(t => t.PartCost).HasColumnType("decimal(18,4)");
        b.Property(t => t.LaborCost).HasColumnType("decimal(18,4)");
        b.HasIndex(t => t.WorkOrderId).HasDatabaseName("IX_MaintenanceTask_WorkOrder");
    }
}

public sealed class FuelLogConfiguration : IEntityTypeConfiguration<FuelLog>
{
    public void Configure(EntityTypeBuilder<FuelLog> b)
    {
        b.ToTable("FuelLog");
        b.HasKey(f => f.FuelLogId);
        b.Property(f => f.OdometerKm).HasColumnType("decimal(12,1)");
        b.Property(f => f.Liters).HasColumnType("decimal(10,3)");
        b.Property(f => f.TotalCost).HasColumnType("decimal(18,4)");
        b.Property(f => f.Station).HasMaxLength(150);
        b.HasIndex(f => new { f.VehicleId, f.FillDateUtc }).HasDatabaseName("IX_FuelLog_Vehicle");
        b.HasOne(f => f.Vehicle).WithMany().HasForeignKey(f => f.VehicleId);
        b.HasOne(f => f.Driver).WithMany().HasForeignKey(f => f.DriverId);
        b.HasOne(f => f.Currency).WithMany().HasForeignKey(f => f.CurrencyLookupId);
    }
}
