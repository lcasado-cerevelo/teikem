using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Clients;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 2 — Clientes y contratos. Mapeo 1:1 con Diseño/logistica-db-estructura.sql (CAPA 5/6 + PortalUser en CAPA 17).
// Los índices únicos filtrados y CHECKs se declaran también en SQL (guardas de última línea); aquí quedan documentados
// para que el modelo EF coincida con el esquema. GeoPoint (GEOGRAPHY) no se mapea.

public sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> b)
    {
        b.ToTable("Client");
        b.HasKey(c => c.ClientId);
        b.Property(c => c.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(c => c.Code).HasMaxLength(30).IsRequired();
        b.Property(c => c.Name).HasMaxLength(200).IsRequired();
        b.Property(c => c.LegalName).HasMaxLength(250);
        b.Property(c => c.TaxId).HasMaxLength(50);
        b.Property(c => c.CreditLimit).HasColumnType("decimal(18,4)");
        b.Property(c => c.OrderNumberFormat).HasMaxLength(40);
        b.Property(c => c.InvoiceNumberFormat).HasMaxLength(40);
        b.Property(c => c.PackageNumberFormat).HasMaxLength(40);
        b.Property(c => c.RowVersion).IsRowVersion();
        b.HasIndex(c => new { c.TenantId, c.Code }).IsUnique().HasDatabaseName("UQ_Client_Tenant_Code");
        b.HasIndex(c => c.PublicId);
        b.HasOne(c => c.Status).WithMany().HasForeignKey(c => c.StatusCodeId);
        b.HasOne(c => c.PaymentTerm).WithMany().HasForeignKey(c => c.PaymentTermLookupId);
        b.HasOne(c => c.Currency).WithMany().HasForeignKey(c => c.CurrencyLookupId);
        // Dos relaciones Client↔Location: el puntero al almacén por defecto y la colección de localizaciones propias.
        b.HasOne(c => c.DefaultPickupLocation).WithMany().HasForeignKey(c => c.DefaultPickupLocationId);
        b.HasMany(c => c.Locations).WithOne(l => l.Client).HasForeignKey(l => l.ClientId);
        b.HasMany(c => c.Contacts).WithOne(x => x.Client).HasForeignKey(x => x.ClientId);
        b.HasMany(c => c.Contracts).WithOne(x => x.Client).HasForeignKey(x => x.ClientId);
    }
}

public sealed class ClientContactConfiguration : IEntityTypeConfiguration<ClientContact>
{
    public void Configure(EntityTypeBuilder<ClientContact> b)
    {
        b.ToTable("ClientContact");
        b.HasKey(c => c.ClientContactId);
        b.Property(c => c.FullName).HasMaxLength(150).IsRequired();
        b.Property(c => c.Role).HasMaxLength(80);
        // Un contacto principal activo por cliente
        b.HasIndex(c => c.ClientId).IsUnique().HasFilter("[IsPrimary] = 1 AND [IsActive] = 1").HasDatabaseName("UX_ClientContact_Primary");
    }
}

public sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.ToTable("Location");
        b.HasKey(l => l.LocationId);
        b.Property(l => l.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(l => l.Code).HasMaxLength(40);
        b.Property(l => l.Name).HasMaxLength(200).IsRequired();
        b.Property(l => l.Line1).HasMaxLength(200).IsRequired();
        b.Property(l => l.Line2).HasMaxLength(200);
        b.Property(l => l.City).HasMaxLength(100).IsRequired();
        b.Property(l => l.State).HasMaxLength(100);
        b.Property(l => l.PostalCode).HasMaxLength(20);
        b.Property(l => l.DefaultWindowStart).HasColumnType("time");
        b.Property(l => l.DefaultWindowEnd).HasColumnType("time");
        b.Property(l => l.AccessNotes).HasMaxLength(500);
        b.Property(l => l.DeliveryNotes).HasMaxLength(500);
        b.Property(l => l.RowVersion).IsRowVersion();
        b.HasIndex(l => new { l.TenantId, l.ClientId }).HasFilter("[IsActive] = 1").HasDatabaseName("IX_Location_Tenant_Client");
        b.HasIndex(l => l.PublicId);
        b.HasOne(l => l.LocationType).WithMany().HasForeignKey(l => l.LocationTypeLookupId);
        b.HasOne(l => l.Country).WithMany().HasForeignKey(l => l.CountryLookupId);
    }
}

public sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> b)
    {
        b.ToTable("Contract");
        b.HasKey(c => c.ContractId);
        b.Property(c => c.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(c => c.ContractNumber).HasMaxLength(40).IsRequired();
        b.Property(c => c.Title).HasMaxLength(200).IsRequired();
        b.Property(c => c.StartDate).HasColumnType("date");
        b.Property(c => c.EndDate).HasColumnType("date");
        b.Property(c => c.CodCommissionPct).HasColumnType("decimal(5,2)");
        b.Property(c => c.DispatchFee).HasColumnType("decimal(18,4)");
        b.Property(c => c.CodFeeValue).HasColumnType("decimal(18,4)");
        b.Property(c => c.RowVersion).IsRowVersion();
        b.HasIndex(c => new { c.TenantId, c.ContractNumber }).IsUnique().HasDatabaseName("UQ_Contract_Number");
        b.HasIndex(c => c.PublicId);
        b.HasOne(c => c.Status).WithMany().HasForeignKey(c => c.StatusCodeId);
        b.HasOne(c => c.Currency).WithMany().HasForeignKey(c => c.CurrencyLookupId);
        b.HasOne(c => c.BillingModel).WithMany().HasForeignKey(c => c.BillingModelLookupId);
        b.HasOne(c => c.CodFeeType).WithMany().HasForeignKey(c => c.CodFeeTypeLookupId);
        b.HasMany(c => c.ServiceLevels).WithOne(l => l.Contract).HasForeignKey(l => l.ContractId);
    }
}

public sealed class ContractServiceLevelConfiguration : IEntityTypeConfiguration<ContractServiceLevel>
{
    public void Configure(EntityTypeBuilder<ContractServiceLevel> b)
    {
        b.ToTable("ContractServiceLevel");
        b.HasKey(l => l.ServiceLevelId);
        b.Property(l => l.OnTimeTargetPct).HasColumnType("decimal(5,2)");
        b.Property(l => l.PenaltyAmount).HasColumnType("decimal(18,4)");
        // Un nivel activo por tipo de servicio y contrato
        b.HasIndex(l => new { l.ContractId, l.ServiceTypeLookupId }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_ContractServiceLevel");
        b.HasOne(l => l.ServiceType).WithMany().HasForeignKey(l => l.ServiceTypeLookupId);
    }
}

public sealed class RateComponentConfiguration : IEntityTypeConfiguration<RateComponent>
{
    public void Configure(EntityTypeBuilder<RateComponent> b)
    {
        b.ToTable("RateComponent");
        b.HasKey(r => r.RateComponentId);
        b.Property(r => r.FlatAmount).HasColumnType("decimal(18,4)");
        b.Property(r => r.UnitAmount).HasColumnType("decimal(18,4)");
        b.Property(r => r.MinCharge).HasColumnType("decimal(18,4)");
        b.Property(r => r.EffectiveFrom).HasColumnType("date");
        b.Property(r => r.EffectiveTo).HasColumnType("date");
        // Una sola fila abierta por (tenant, contrato, tipo, servicio, paquete)
        b.HasIndex(r => new { r.TenantId, r.ContractId, r.ComponentTypeLookupId, r.ServiceTypeLookupId, r.PackageTypeLookupId })
            .IsUnique().HasFilter("[EffectiveTo] IS NULL AND [IsActive] = 1").HasDatabaseName("UQ_RateComponent_Open");
        b.HasIndex(r => r.ContractId).HasFilter("[ContractId] IS NOT NULL").HasDatabaseName("IX_RateComponent_Contract");
        b.HasOne(r => r.Contract).WithMany().HasForeignKey(r => r.ContractId);
        b.HasMany(r => r.Tiers).WithOne(t => t.Component).HasForeignKey(t => t.RateComponentId);
    }
}

public sealed class RateTierConfiguration : IEntityTypeConfiguration<RateTier>
{
    public void Configure(EntityTypeBuilder<RateTier> b)
    {
        b.ToTable("RateTier");
        b.HasKey(t => t.RateTierId);
        b.Property(t => t.MinValue).HasColumnType("decimal(14,3)");
        b.Property(t => t.MaxValue).HasColumnType("decimal(14,3)");
        b.Property(t => t.UnitAmount).HasColumnType("decimal(18,4)");
        b.Property(t => t.FlatAmount).HasColumnType("decimal(18,4)");
        b.Property(t => t.EffectiveFrom).HasColumnType("date");
        b.Property(t => t.EffectiveTo).HasColumnType("date");
    }
}

public sealed class SpecialServiceTypeConfiguration : IEntityTypeConfiguration<SpecialServiceType>
{
    public void Configure(EntityTypeBuilder<SpecialServiceType> b)
    {
        b.ToTable("SpecialServiceType");
        b.HasKey(t => t.SpecialServiceTypeId);
        b.Property(t => t.Name).HasMaxLength(120).IsRequired();
        b.HasIndex(t => new { t.TenantId, t.Name }).IsUnique().HasDatabaseName("UQ_SpecialServiceType");
    }
}

public sealed class SpecialServiceConfiguration : IEntityTypeConfiguration<SpecialService>
{
    public void Configure(EntityTypeBuilder<SpecialService> b)
    {
        b.ToTable("SpecialService");
        b.HasKey(s => s.SpecialServiceId);
        b.Property(s => s.Rate).HasColumnType("decimal(18,4)");
        b.Property(s => s.EffectiveFrom).HasColumnType("date");
        b.Property(s => s.EffectiveTo).HasColumnType("date");
        // Una sola fila abierta por cliente y tipo
        b.HasIndex(s => new { s.ClientId, s.SpecialServiceTypeId }).IsUnique().HasFilter("[EffectiveTo] IS NULL AND [IsActive] = 1").HasDatabaseName("UQ_SpecialService_Open");
        b.HasIndex(s => new { s.TenantId, s.ClientId }).HasDatabaseName("IX_SpecialService_Client");
        b.HasOne(s => s.Client).WithMany().HasForeignKey(s => s.ClientId);
        b.HasOne(s => s.Type).WithMany().HasForeignKey(s => s.SpecialServiceTypeId);
    }
}

public sealed class PortalUserConfiguration : IEntityTypeConfiguration<PortalUser>
{
    public void Configure(EntityTypeBuilder<PortalUser> b)
    {
        b.ToTable("PortalUser");
        b.HasKey(p => p.PortalUserId);
        b.Property(p => p.Email).HasMaxLength(150).IsRequired();
        b.Property(p => p.FullName).HasMaxLength(150);
        b.HasIndex(p => new { p.TenantId, p.Email }).IsUnique().HasDatabaseName("UQ_PortalUser");
        b.HasOne(p => p.Client).WithMany().HasForeignKey(p => p.ClientId);
        b.HasOne(p => p.Role).WithMany().HasForeignKey(p => p.RoleLookupId);
        b.HasOne(p => p.Status).WithMany().HasForeignKey(p => p.StatusCodeId);
        b.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId);
    }
}
