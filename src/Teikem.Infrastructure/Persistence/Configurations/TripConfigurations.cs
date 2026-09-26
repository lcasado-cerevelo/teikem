using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Trips;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 5 — Trips y rutas. Mapeo 1:1 con Diseño/logistica-db-estructura.sql (CAPA 12 Trip/TripOrder/Route/RouteStop y CAPA 12B
// OptimizationRun). Los índices únicos (filtrados o no) y los CHECKs viven en SQL como guardas de última línea; aquí se espejan
// con el mismo nombre y filtro para que el modelo EF coincida con el esquema. Las FKs compuestas (Id, TenantId) del SQL NO se
// mapean: EF sigue las relaciones por columna simple, sin cascadas (NoAction), y nunca genera esquema. OriginWarehouseId se
// mapea sin navegación (Warehouse no está mapeado). Los DEFAULT del SQL (IsCurrent, CreatedAtUtc, AssignedAtUtc...) no se
// declaran aquí: la aplicación siempre escribe esos valores (RouteWriter, interceptor de auditoría). GeoPoint (GEOGRAPHY) y DriverLocationPing no se mapean: se leen y
// escriben solo con SQL crudo confinado en Trips/TripQueries.cs (con 'TenantId =').

public sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> b)
    {
        b.ToTable("Trip");
        b.HasKey(t => t.TripId);
        b.Property(t => t.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(t => t.Code).HasMaxLength(40).IsRequired();
        b.Property(t => t.PlanDate).HasColumnType("date");
        b.Property(t => t.TotalDistanceKm).HasColumnType("decimal(12,3)");
        b.Property(t => t.RowVersion).IsRowVersion();

        // Número irrepetible por compañía (sin filtro: una ruta eliminada no libera su número).
        b.HasIndex(t => new { t.TenantId, t.Code }).IsUnique().HasDatabaseName("UQ_Trip_Code");
        b.HasIndex(t => new { t.TenantId, t.PlanDate }).HasFilter("[IsActive] = 1").HasDatabaseName("IX_Trip_Tenant_Date");
        b.HasIndex(t => new { t.TenantId, t.PlanDate, t.DispatchZoneId }).HasFilter("[IsActive] = 1").HasDatabaseName("IX_Trip_Zone_Date");
        b.HasIndex(t => new { t.TenantId, t.DriverId, t.PlanDate }).HasFilter("[IsActive] = 1 AND [DriverId] IS NOT NULL").HasDatabaseName("IX_Trip_Driver_Date");

        b.HasOne(t => t.Status).WithMany().HasForeignKey(t => t.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(t => t.DispatchZone).WithMany().HasForeignKey(t => t.DispatchZoneId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(t => t.Driver).WithMany().HasForeignKey(t => t.DriverId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(t => t.Vehicle).WithMany().HasForeignKey(t => t.VehicleId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(t => t.Orders).WithOne(o => o.Trip).HasForeignKey(o => o.TripId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(t => t.Routes).WithOne(r => r.Trip).HasForeignKey(r => r.TripId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class TripOrderConfiguration : IEntityTypeConfiguration<TripOrder>
{
    public void Configure(EntityTypeBuilder<TripOrder> b)
    {
        b.ToTable("TripOrder");
        b.HasKey(o => o.TripOrderId);

        b.HasIndex(o => new { o.TripId, o.TransportOrderId }).IsUnique().HasDatabaseName("UQ_TripOrder");
        // Una orden solo puede estar en UNA ruta vigente (última línea contra asignaciones simultáneas).
        b.HasIndex(o => o.TransportOrderId).IsUnique().HasFilter("[IsCurrent] = 1").HasDatabaseName("UX_TripOrder_Current");
        b.HasIndex(o => o.TripId).HasFilter("[IsCurrent] = 1").HasDatabaseName("IX_TripOrder_Trip");

        b.HasOne<Teikem.Domain.Orders.TransportOrder>().WithMany().HasForeignKey(o => o.TransportOrderId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class RouteConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> b)
    {
        b.ToTable("Route");
        b.HasKey(r => r.RouteId);
        b.Property(r => r.TotalDistanceKm).HasColumnType("decimal(12,3)");
        b.Property(r => r.RowVersion).IsRowVersion();

        // A lo sumo una versión vigente por Trip y versiones únicas por Trip.
        b.HasIndex(r => r.TripId).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_Route_Trip_Active");
        b.HasIndex(r => new { r.TripId, r.Version }).IsUnique().HasDatabaseName("UQ_Route_Version");

        b.HasOne(r => r.Status).WithMany().HasForeignKey(r => r.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(r => r.Stops).WithOne(s => s.Route).HasForeignKey(s => s.RouteId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class RouteStopConfiguration : IEntityTypeConfiguration<RouteStop>
{
    public void Configure(EntityTypeBuilder<RouteStop> b)
    {
        b.ToTable("RouteStop");
        b.HasKey(s => s.RouteStopId);
        b.Property(s => s.DistanceFromPrevKm).HasColumnType("decimal(12,3)");

        b.HasIndex(s => new { s.RouteId, s.OrderStopId }).IsUnique().HasDatabaseName("UQ_RouteStop");
        b.HasIndex(s => new { s.RouteId, s.Sequence }).HasDatabaseName("IX_RouteStop_Route");

        b.HasOne(s => s.Status).WithMany().HasForeignKey(s => s.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Teikem.Domain.Orders.OrderStop>().WithMany().HasForeignKey(s => s.OrderStopId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class OptimizationRunConfiguration : IEntityTypeConfiguration<OptimizationRun>
{
    public void Configure(EntityTypeBuilder<OptimizationRun> b)
    {
        b.ToTable("OptimizationRun");
        b.HasKey(r => r.OptimizationRunId);
        b.Property(r => r.RequestJson).IsRequired();
        b.Property(r => r.TotalDistanceKm).HasColumnType("decimal(12,3)");

        b.HasIndex(r => new { r.TenantId, r.TripId, r.StartedAtUtc }).HasDatabaseName("IX_OptimizationRun_Trip");

        b.HasOne(r => r.Engine).WithMany().HasForeignKey(r => r.EngineLookupId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(r => r.Status).WithMany().HasForeignKey(r => r.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Trip>().WithMany().HasForeignKey(r => r.TripId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Route>().WithMany().HasForeignKey(r => r.RouteId).OnDelete(DeleteBehavior.NoAction);
    }
}
