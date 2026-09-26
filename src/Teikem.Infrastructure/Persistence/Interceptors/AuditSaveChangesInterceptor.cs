using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Audit;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;

namespace Teikem.Infrastructure.Persistence.Interceptors;

/// <summary>Cambio pendiente de auditar; el EntityId se resuelve después del SaveChanges (llaves IDENTITY).</summary>
internal sealed class PendingAudit
{
    public required EntityEntry Entry { get; init; }
    public required string EntityTypeCode { get; init; }
    public required string Action { get; init; }
    public required int TenantId { get; init; }
    public required Dictionary<string, object?> Changes { get; init; }
}

/// <summary>
/// Plano 1 de auditoría (E): detecta entidades [AuditEntity] modificadas, calcula el diff campo a campo y escribe
/// AuditLog con el CorrelationId del request — sin que cada controlador se acuerde. Campos [SensitiveData] y
/// [NotAudited] quedan fuera por convención de mapeo. También estampa CreatedBy/UpdatedBy y TenantId.
/// </summary>
public sealed class AuditSaveChangesInterceptor(ITenantContext tenant, ILookupCache lookups, ILogger<AuditSaveChangesInterceptor> logger)
    : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Prepare(eventData.Context as TeikemDbContext);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Prepare(eventData.Context as TeikemDbContext);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        FlushAsync(eventData.Context as TeikemDbContext, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await FlushAsync(eventData.Context as TeikemDbContext, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    // Un SaveChanges fallido o cancelado descarta su bitácora pendiente: si el mismo DbContext se reutiliza (importador: una
    // transacción por fila), el siguiente SaveChanges exitoso no debe escribir AuditLog de cambios que nunca se guardaron.
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        DiscardPending(eventData.Context as TeikemDbContext);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        DiscardPending(eventData.Context as TeikemDbContext);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        DiscardPending(eventData.Context as TeikemDbContext);
        base.SaveChangesCanceled(eventData);
    }

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        DiscardPending(eventData.Context as TeikemDbContext);
        return base.SaveChangesCanceledAsync(eventData, cancellationToken);
    }

    private static void DiscardPending(TeikemDbContext? ctx)
    {
        if (ctx is null || ctx.SuppressAudit) return;
        ctx.PendingAudits.Clear();
    }

    private void Prepare(TeikemDbContext? ctx)
    {
        if (ctx is null || ctx.SuppressAudit) return;
        var now = DateTime.UtcNow;
        ctx.ChangeTracker.DetectChanges();

        foreach (var entry in ctx.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            // --- Estampado automático (auditoría inherente) ---
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity is ITenantScoped ts && ts.TenantId == 0 && tenant.TenantId.HasValue) ts.TenantId = tenant.TenantId.Value;
                if (entry.Entity is IAuditStamped cs)
                {
                    if (cs.CreatedAtUtc == default) cs.CreatedAtUtc = now;
                    cs.CreatedBy ??= tenant.UserId;
                }
            }
            else if (entry.State == EntityState.Modified && entry.Entity is IAuditStamped us)
            {
                us.UpdatedAtUtc = now;
                us.UpdatedBy = tenant.UserId;
            }

            // --- Diff para AuditLog ---
            var attr = entry.Metadata.ClrType.GetCustomAttribute<AuditEntityAttribute>();
            if (attr is null) continue;

            var tenantId = ResolveTenantId(entry.Entity);
            if (tenantId is null) continue; // sin tenant (p. ej. seeding global): no hay dónde colgar la bitácora

            var (action, changes) = BuildChanges(entry);
            if (changes.Count == 0 && action == AuditActions.Update) continue;

            ctx.PendingAudits.Add(new PendingAudit
            {
                Entry = entry, EntityTypeCode = attr.EntityTypeCode, Action = action, TenantId = tenantId.Value, Changes = changes,
            });
        }
    }

    private int? ResolveTenantId(object entity) => entity switch
    {
        ITenantScoped ts when ts.TenantId > 0 => ts.TenantId,
        IOptionallyTenantScoped ots when ots.TenantId.HasValue => ots.TenantId,
        _ => tenant.TenantId,
    };

    private static (string Action, Dictionary<string, object?> Changes) BuildChanges(EntityEntry entry)
    {
        var changes = new Dictionary<string, object?>();
        var isSoftDeletable = entry.Entity is ISoftDeletable;

        foreach (var prop in entry.Properties)
        {
            var clrProp = prop.Metadata.PropertyInfo;
            if (clrProp is null) continue;
            if (clrProp.GetCustomAttribute<SensitiveDataAttribute>() is not null) continue;
            if (clrProp.GetCustomAttribute<NotAuditedAttribute>() is not null) continue;
            if (prop.Metadata.IsConcurrencyToken) continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    changes[prop.Metadata.Name] = prop.CurrentValue;
                    break;
                case EntityState.Deleted:
                    changes[prop.Metadata.Name] = prop.OriginalValue;
                    break;
                case EntityState.Modified when prop.IsModified && !Equals(prop.OriginalValue, prop.CurrentValue):
                    changes[prop.Metadata.Name] = new { from = prop.OriginalValue, to = prop.CurrentValue };
                    break;
            }
        }

        var action = entry.State switch
        {
            EntityState.Added => AuditActions.Create,
            EntityState.Deleted => AuditActions.Delete,
            _ => AuditActions.Update,
        };

        // Soft-delete/restore se distinguen del update genérico
        if (entry.State == EntityState.Modified && isSoftDeletable)
        {
            var p = entry.Property(nameof(ISoftDeletable.IsActive));
            if (p.IsModified && p.OriginalValue is bool was && p.CurrentValue is bool now && was != now)
                action = now ? AuditActions.Restore : AuditActions.Delete;
        }
        return (action, changes);
    }

    private async Task FlushAsync(TeikemDbContext? ctx, CancellationToken ct)
    {
        if (ctx is null || ctx.SuppressAudit || ctx.PendingAudits.Count == 0) return;
        var pending = ctx.PendingAudits.ToList();
        ctx.PendingAudits.Clear();

        try
        {
            var actionIds = new Dictionary<string, int>();
            var rows = new List<AuditLog>();
            foreach (var p in pending)
            {
                var entityTypeId = await lookups.TryGetIdAsync(LookupDomains.EntityType, p.EntityTypeCode, ct);
                if (entityTypeId is null)
                {
                    logger.LogWarning("AuditLog: EntityType '{Code}' no existe en el catálogo; se omite la bitácora.", p.EntityTypeCode);
                    continue;
                }
                if (!actionIds.TryGetValue(p.Action, out var actionId))
                    actionIds[p.Action] = actionId = await lookups.GetIdAsync(LookupDomains.AuditAction, p.Action, ct);

                var (entityId, keyJson) = ResolveKey(p.Entry);
                if (keyJson is not null) p.Changes["$key"] = keyJson;

                rows.Add(new AuditLog
                {
                    TenantId = p.TenantId,
                    EntityTypeLookupId = entityTypeId.Value,
                    EntityId = entityId,
                    ActionLookupId = actionId,
                    UserId = tenant.UserId,
                    ChangesJson = JsonSerializer.Serialize(p.Changes, Json),
                    CorrelationId = tenant.CorrelationId,
                    IpAddress = tenant.IpAddress,
                    UserAgent = tenant.UserAgent is { Length: > 300 } ua ? ua[..300] : tenant.UserAgent,
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }
            if (rows.Count == 0) return;

            ctx.SuppressAudit = true;
            ctx.AuditLogs.AddRange(rows);
            await ctx.SaveChangesAsync(ct);
        }
        finally
        {
            ctx.SuppressAudit = false;
        }
    }

    /// <summary>PK entera simple → EntityId; llaves compuestas/no numéricas → 0 + llave serializada en el diff.</summary>
    private static (int EntityId, object? KeyJson) ResolveKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return (0, null);
        if (key.Properties.Count == 1)
        {
            var v = entry.Property(key.Properties[0].Name).CurrentValue;
            return v switch
            {
                int i => (i, null),
                long l => (unchecked((int)l), l),
                _ => (0, v),
            };
        }
        var dict = key.Properties.ToDictionary(p => p.Name, p => entry.Property(p.Name).CurrentValue);
        return (0, dict);
    }
}
