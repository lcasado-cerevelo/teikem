using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Lote 114 — las marcas («pick up», «facturable») de un tipo de servicio: el ExtraJson del valor y, encima, el ajuste de la compañía.</summary>
public static class ServiceTypeMarkReader
{
    public static async Task<ServiceTypeMarks> LoadAsync(TeikemDbContext db, int serviceTypeLookupId, CancellationToken ct)
    {
        var extra = await db.LookupCodes.AsNoTracking().Where(l => l.LookupCodeId == serviceTypeLookupId).Select(l => l.ExtraJson).FirstOrDefaultAsync(ct);
        var marks = ServiceTypeMarks.Parse(extra);
        var custom = await db.LookupCodeOverrides.AsNoTracking().Where(o => o.LookupCodeId == serviceTypeLookupId).Select(o => o.CustomExtraJson).FirstOrDefaultAsync(ct);
        return marks.Apply(custom);
    }
}
