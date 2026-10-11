using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// 2026-10-11 (b) — Motivo por default del despacho manual: ¿un motivo del catálogo ManualIssueReason se puede usar en la
/// compañía? Activo, visible para ella (global o propio: filtro de tenant de LookupCode) y no deshabilitado por su override
/// (LookupCodeOverride, filtrado por tenant). La misma regla con que PickBatchService acepta el reasonCode de un despacho. Se lee
/// de la base (no de ILookupCache, que es de todas las compañías y no ve los overrides): un cambio en Sistema → Catálogos se
/// nota de inmediato.
/// </summary>
public static class ManualIssueReasonLookup
{
    /// <summary>Id del motivo por código si se puede usar en la compañía; null si no existe, está inactivo o deshabilitado.</summary>
    public static async Task<int?> FindUsableIdAsync(TeikemDbContext db, string code, CancellationToken ct)
    {
        var id = await db.LookupCodes.AsNoTracking()
            .Where(l => l.Entity == LookupDomains.ManualIssueReason && l.InternalCode == code && l.IsActive)
            .Select(l => (int?)l.LookupCodeId).FirstOrDefaultAsync(ct);
        if (id is null) return null;
        return await IsDisabledAsync(db, id.Value, ct) ? null : id;
    }

    /// <summary>Código del motivo guardado si todavía se puede usar en la compañía; null si no hay o ya no (se ignora).</summary>
    public static async Task<string?> UsableCodeAsync(TeikemDbContext db, int? lookupCodeId, CancellationToken ct)
    {
        if (lookupCodeId is not int id) return null;
        var code = await db.LookupCodes.AsNoTracking()
            .Where(l => l.LookupCodeId == id && l.Entity == LookupDomains.ManualIssueReason && l.IsActive)
            .Select(l => l.InternalCode).FirstOrDefaultAsync(ct);
        if (code is null) return null;
        return await IsDisabledAsync(db, id, ct) ? null : code;
    }

    private static Task<bool> IsDisabledAsync(TeikemDbContext db, int lookupCodeId, CancellationToken ct)
        => db.LookupCodeOverrides.AsNoTracking().AnyAsync(o => o.LookupCodeId == lookupCodeId && !o.IsEnabled, ct);
}
