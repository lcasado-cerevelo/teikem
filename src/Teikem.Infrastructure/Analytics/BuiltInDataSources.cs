using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>Fuente: bitácora de cambios (AuditLog). Actividad de período → tiene rango de fecha.</summary>
public sealed class AuditLogDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.AuditLog;
    public string LabelEs => "Bitácora de cambios";
    public string LabelEn => "Audit log";
    public string? EntityTypeCode => null;
    public string? DateField => "CreatedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Operations;
    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("CreatedAtUtc", "Fecha", "Date", DataFieldType.Date),
        new DataField("EntityType", "Entidad", "Entity", DataFieldType.Text),
        new DataField("EntityTypeCode", "Código de entidad", "Entity code", DataFieldType.Text),
        new DataField("EntityId", "Id de registro", "Record id", DataFieldType.Number),
        new DataField("Action", "Acción", "Action", DataFieldType.Text),
        new DataField("ActionCode", "Código de acción", "Action code", DataFieldType.Text),
        new DataField("UserId", "Id de usuario", "User id", DataFieldType.Number),
        new DataField("UserName", "Usuario", "User", DataFieldType.Text),
        new DataField("CorrelationId", "Correlación", "Correlation", DataFieldType.Text),
    };
    public IReadOnlyList<DataRelation> Relations { get; } = new[] { new DataRelation("User", EntityTypes.User, "UserId", "Usuario", "User") };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (q.FromUtc.HasValue) query = query.Where(a => a.CreatedAtUtc >= q.FromUtc.Value);
        if (q.ToUtc.HasValue) query = query.Where(a => a.CreatedAtUtc < q.ToUtc.Value);
        var items = await query.OrderByDescending(a => a.AuditLogId).Take(20000)
            .Select(a => new { a.AuditLogId, a.CreatedAtUtc, a.EntityTypeLookupId, a.EntityId, a.ActionLookupId, a.UserId, a.CorrelationId })
            .ToListAsync(ct);
        var userIds = items.Where(i => i.UserId.HasValue).Select(i => i.UserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email }).ToDictionaryAsync(u => u.Id, ct);
        var rows = new List<DataRow>(items.Count);
        foreach (var i in items)
        {
            var et = await lookups.GetAsync(i.EntityTypeLookupId, ct);
            var ac = await lookups.GetAsync(i.ActionLookupId, ct);
            rows.Add(new DataRow
            {
                ["Id"] = i.AuditLogId, ["CreatedAtUtc"] = i.CreatedAtUtc,
                ["EntityType"] = MultilingualText.Resolve(et?.LabelJson, tenant.Lang), ["EntityTypeCode"] = et?.InternalCode, ["EntityId"] = i.EntityId,
                ["Action"] = MultilingualText.Resolve(ac?.LabelJson, tenant.Lang), ["ActionCode"] = ac?.InternalCode, ["UserId"] = i.UserId,
                ["UserName"] = i.UserId.HasValue && users.TryGetValue(i.UserId.Value, out var u) ? (u.FullName ?? u.Email) : null,
                ["CorrelationId"] = i.CorrelationId?.ToString(),
            });
        }
        return rows;
    }
}

/// <summary>Fuente: eventos de seguridad (SecurityEvent).</summary>
public sealed class SecurityEventDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.SecurityEvent;
    public string LabelEs => "Eventos de seguridad";
    public string LabelEn => "Security events";
    public string? EntityTypeCode => null;
    public string? DateField => "CreatedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Operations;
    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("CreatedAtUtc", "Fecha", "Date", DataFieldType.Date),
        new DataField("EventType", "Evento", "Event", DataFieldType.Text),
        new DataField("EventTypeCode", "Código de evento", "Event code", DataFieldType.Text),
        new DataField("Outcome", "Resultado", "Outcome", DataFieldType.Text),
        new DataField("OutcomeCode", "Código de resultado", "Outcome code", DataFieldType.Text),
        new DataField("UserId", "Id de usuario", "User id", DataFieldType.Number),
        new DataField("UserName", "Usuario", "User", DataFieldType.Text),
        new DataField("IpAddress", "IP", "IP", DataFieldType.Text),
    };
    public IReadOnlyList<DataRelation> Relations { get; } = new[] { new DataRelation("User", EntityTypes.User, "UserId", "Usuario", "User") };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.SecurityEvents.AsNoTracking().AsQueryable();
        if (q.FromUtc.HasValue) query = query.Where(a => a.CreatedAtUtc >= q.FromUtc.Value);
        if (q.ToUtc.HasValue) query = query.Where(a => a.CreatedAtUtc < q.ToUtc.Value);
        var items = await query.OrderByDescending(a => a.SecurityEventId).Take(20000)
            .Select(a => new { a.SecurityEventId, a.CreatedAtUtc, a.EventTypeLookupId, a.OutcomeLookupId, a.UserId, a.IpAddress })
            .ToListAsync(ct);
        var userIds = items.Where(i => i.UserId.HasValue).Select(i => i.UserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email }).ToDictionaryAsync(u => u.Id, ct);
        var rows = new List<DataRow>(items.Count);
        foreach (var i in items)
        {
            var et = await lookups.GetAsync(i.EventTypeLookupId, ct);
            var oc = await lookups.GetAsync(i.OutcomeLookupId, ct);
            rows.Add(new DataRow
            {
                ["Id"] = i.SecurityEventId, ["CreatedAtUtc"] = i.CreatedAtUtc,
                ["EventType"] = MultilingualText.Resolve(et?.LabelJson, tenant.Lang), ["EventTypeCode"] = et?.InternalCode,
                ["Outcome"] = MultilingualText.Resolve(oc?.LabelJson, tenant.Lang), ["OutcomeCode"] = oc?.InternalCode,
                ["UserId"] = i.UserId,
                ["UserName"] = i.UserId.HasValue && users.TryGetValue(i.UserId.Value, out var u) ? (u.FullName ?? u.Email) : null,
                ["IpAddress"] = i.IpAddress,
            });
        }
        return rows;
    }
}

/// <summary>Fuente: usuarios del tenant (estado actual → sin rango de fecha). Admite campos personalizados de USER.</summary>
public sealed class UserDataSource(TeikemDbContext db, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.User;
    public string LabelEs => "Usuarios";
    public string LabelEn => "Users";
    public string? EntityTypeCode => EntityTypes.User;
    public string? DateField => null;
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Operations;
    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("FullName", "Nombre", "Name", DataFieldType.Text),
        new DataField("Email", "Correo", "Email", DataFieldType.Text),
        new DataField("IsActive", "Activo", "Active", DataFieldType.Bool),
        new DataField("MembershipStatus", "Membresía", "Membership", DataFieldType.Text),
        new DataField("Roles", "Roles", "Roles", DataFieldType.Text),
        new DataField("LastLoginUtc", "Último acceso", "Last login", DataFieldType.Date),
        new DataField("MfaEnabled", "MFA", "MFA", DataFieldType.Bool),
    };
    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? -1;
        var memberships = await db.UserTenants.AsNoTracking().Where(m => m.TenantId == tenantId)
            .Select(m => new { m.UserId, Status = m.Status!.InternalCode }).ToListAsync(ct);
        var ids = memberships.Select(m => m.UserId).ToList();
        if (q.Ids is not null) ids = ids.Intersect(q.Ids).ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Email, u.IsActive, u.LastLoginUtc, u.TwoFactorEnabled }).ToListAsync(ct);
        var roles = await db.AppUserRoles.AsNoTracking().Where(r => r.TenantId == tenantId && ids.Contains(r.UserId))
            .Select(r => new { r.UserId, r.Role!.Name }).ToListAsync(ct);
        var rolesBy = roles.ToLookup(r => r.UserId, r => r.Name);
        var statusBy = memberships.ToDictionary(m => m.UserId, m => m.Status);
        return users.Select(u => new DataRow
        {
            ["Id"] = u.Id, ["FullName"] = u.FullName, ["Email"] = u.Email, ["IsActive"] = u.IsActive,
            ["MembershipStatus"] = statusBy.GetValueOrDefault(u.Id), ["Roles"] = string.Join(", ", rolesBy[u.Id]),
            ["LastLoginUtc"] = u.LastLoginUtc, ["MfaEnabled"] = u.TwoFactorEnabled,
        }).ToList();
    }
}
