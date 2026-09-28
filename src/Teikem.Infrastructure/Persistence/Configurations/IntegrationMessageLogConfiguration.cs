using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Entities;

namespace Teikem.Infrastructure.Persistence.Configurations;

/// <summary>
/// dbo.IntegrationMessageLog (módulo 13) con las columnas de idempotencia del Lote 8A; índice único filtrado
/// UX_IntegrationLog_Idem (TenantId, UserId, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL.
/// </summary>
public sealed class IntegrationMessageLogConfiguration : IEntityTypeConfiguration<IntegrationMessageLog>
{
    public void Configure(EntityTypeBuilder<IntegrationMessageLog> b)
    {
        b.ToTable("IntegrationMessageLog");
        b.HasKey(x => x.IntegrationMessageLogId);
        b.Property(x => x.Endpoint).HasMaxLength(200);
        // Collation binaria (como el SQL): la clave distingue mayúsculas.
        b.Property(x => x.IdempotencyKey).HasMaxLength(80).UseCollation("Latin1_General_100_BIN2");
        b.Property(x => x.RequestHash).HasMaxLength(64);
        b.Property(x => x.Method).HasMaxLength(8);
        b.HasIndex(x => new { x.TenantId, x.UserId, x.IdempotencyKey }).IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL").HasDatabaseName("UX_IntegrationLog_Idem");
    }
}
