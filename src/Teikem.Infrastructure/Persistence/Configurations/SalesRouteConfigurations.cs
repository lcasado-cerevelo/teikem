using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Sales;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 244 (Lote A del plan de rutas) — personas de la ruta. Mapeo 1:1 con la sección «Lote 244» de logistica-db-update.sql.
// Las FK compuestas (SalesRouteId, TenantId) → dbo.SalesRoute(SalesRouteId, TenantId) viven solo en el SQL (como FK_Product_Client); los índices
// se declaran aquí como documentación (no hay migraciones EF). Las columnas nuevas de Client y SalesOrder se mapean por convención.

public sealed class SalesRouteAssignmentConfiguration : IEntityTypeConfiguration<SalesRouteAssignment>
{
    public void Configure(EntityTypeBuilder<SalesRouteAssignment> b)
    {
        b.ToTable("SalesRouteAssignment");
        b.HasKey(x => x.SalesRouteAssignmentId);
        b.Property(x => x.RowVersion).IsRowVersion();
        // un solo titular vigente por ruta; una sola fila vigente por persona y ruta (una anulada siempre lleva ValidTo)
        b.HasIndex(x => new { x.TenantId, x.SalesRouteId }).HasDatabaseName("UX_SalesRouteAssignment_Primary").IsUnique().HasFilter("[IsPrimary] = 1 AND [ValidTo] IS NULL");
        b.HasIndex(x => new { x.TenantId, x.SalesRouteId, x.UserId }).HasDatabaseName("UX_SalesRouteAssignment_Open").IsUnique().HasFilter("[ValidTo] IS NULL");
        b.HasIndex(x => new { x.TenantId, x.UserId }).HasDatabaseName("IX_SalesRouteAssignment_User").HasFilter("[ValidTo] IS NULL");
        b.HasIndex(x => new { x.TenantId, x.SalesRouteId, x.ValidFrom }).HasDatabaseName("IX_SalesRouteAssignment_Route");
    }
}
