using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — escrituras de las personas de la ruta, compartidas por el servicio de rutas y la configuración del análisis (titular
/// automático). Las llama quien ya abrió la transacción (RunInTransactionAsync) y ya validó lo que es de su pantalla (permiso, ruta activa, persona activa, fechas):
/// aquí va solo la mecánica y los choques con el estado actual (409). Cada paso guarda con SaveGuardedAsync: un choque con los índices únicos filtrados (dos titulares
/// vigentes, la misma persona dos veces) o con RowVersion da 409 «Otra persona cambió las personas de la ruta…».
/// <para>
/// Es pública (y no interna) para que las pruebas la usen directamente; el proyecto de infraestructura no expone sus internos a las pruebas.
/// </para>
/// </summary>
public static class SalesRouteAssignmentOps
{
    /// <summary>
    /// Pone a la persona como TITULAR desde <paramref name="validFrom"/>: cierra el titular vigente con ValidTo = validFrom (y guarda); si la persona era apoyo vigente
    /// de la ruta, cierra esa fila (y guarda); inserta la nueva y guarda. 409 si ya es la titular.
    /// </summary>
    public static async Task<SalesRouteAssignment> OpenPrimaryAsync(TeikemDbContext db, int tenantId, int routeId, int userId, DateOnly validFrom, int? actorUserId,
        DateTime nowUtc, CancellationToken ct)
    {
        var open = await OpenRowsAsync(db, tenantId, routeId, ct);
        var current = open.FirstOrDefault(a => a.IsPrimary);
        if (current is not null && current.UserId == userId) throw new ConflictException(SalesRouteRules.AlreadyPrimary);
        if (current is not null)
        {
            // defensa: el titular nuevo no empieza antes que el actual (CK_SalesRouteAssignment_Dates); el servicio ya lo valida con CheckValidFrom
            if (validFrom < current.ValidFrom) throw new ValidationException("validFrom", SalesRouteRules.ValidFromBeforePrimary(current.ValidFrom));
            Close(current, validFrom, actorUserId, nowUtc);
            await db.SaveGuardedAsync(SalesRouteRules.PeopleChangedConcurrently, ct);
        }
        var support = open.FirstOrDefault(a => !a.IsPrimary && a.UserId == userId);
        if (support is not null)
        {
            Close(support, validFrom < support.ValidFrom ? support.ValidFrom : validFrom, actorUserId, nowUtc);
            await db.SaveGuardedAsync(SalesRouteRules.PeopleChangedConcurrently, ct);
        }
        return await InsertAsync(db, tenantId, routeId, userId, true, validFrom, actorUserId, nowUtc, ct);
    }

    /// <summary>Pone a la persona como APOYO desde <paramref name="validFrom"/>. 409 si es la titular vigente o ya es apoyo vigente.</summary>
    public static async Task<SalesRouteAssignment> OpenSupportAsync(TeikemDbContext db, int tenantId, int routeId, int userId, DateOnly validFrom, int? actorUserId,
        DateTime nowUtc, CancellationToken ct)
    {
        var open = await OpenRowsAsync(db, tenantId, routeId, ct);
        var mine = open.FirstOrDefault(a => a.UserId == userId);
        if (mine is not null) throw new ConflictException(mine.IsPrimary ? SalesRouteRules.PrimaryCannotBeSupport : SalesRouteRules.AlreadySupport);
        return await InsertAsync(db, tenantId, routeId, userId, false, validFrom, actorUserId, nowUtc, ct);
    }

    /// <summary>
    /// Cierra una asignación con ValidTo = <paramref name="validTo"/> (día de la compañía). 404 si no existe en la compañía; 409 si ya está anulada o cerrada;
    /// 400 si la fecha es anterior al inicio o futura.
    /// </summary>
    public static async Task<SalesRouteAssignment> CloseAsync(TeikemDbContext db, int tenantId, int assignmentId, DateOnly validTo, DateOnly today, int? actorUserId,
        DateTime nowUtc, CancellationToken ct)
    {
        var a = await FindAsync(db, tenantId, assignmentId, ct);
        if (!a.IsActive) throw new ConflictException(SalesRouteRules.AssignmentVoided);
        if (a.ValidTo is not null) throw new ConflictException(SalesRouteRules.AssignmentClosed);
        if (SalesRouteRules.CheckClose(a.ValidFrom, validTo, today) is string error) throw new ValidationException("validTo", error);
        Close(a, validTo, actorUserId, nowUtc);
        await db.SaveGuardedAsync(SalesRouteRules.PeopleChangedConcurrently, ct);
        return a;
    }

    /// <summary>
    /// Anula una asignación registrada por error: IsActive 0 y ValidTo ??= ValidFrom (así no ocupa los índices de vigentes). Deja de contar en el historial y en la
    /// atribución. 404 si no existe en la compañía; 409 si ya está anulada.
    /// </summary>
    public static async Task<SalesRouteAssignment> VoidAsync(TeikemDbContext db, int tenantId, int assignmentId, int? actorUserId, DateTime nowUtc, CancellationToken ct)
    {
        var a = await FindAsync(db, tenantId, assignmentId, ct);
        if (!a.IsActive) throw new ConflictException(SalesRouteRules.AssignmentVoided);
        a.IsActive = false;
        a.ValidTo ??= a.ValidFrom;
        a.VoidedAtUtc = nowUtc;
        a.VoidedBy = actorUserId;
        await db.SaveGuardedAsync(SalesRouteRules.PeopleChangedConcurrently, ct);
        return a;
    }

    /// <summary>
    /// Titular automático: si la ruta existe, está activa y no tiene titular vigente, pone a la persona como titular desde <paramref name="validFrom"/> y devuelve true.
    /// Si la ruta está inactiva (o no existe) o ya tiene titular, no hace nada y devuelve false (el administrador decide).
    /// </summary>
    public static async Task<bool> OpenPrimaryIfVacantAsync(TeikemDbContext db, int tenantId, int routeId, int userId, DateOnly validFrom, int? actorUserId,
        DateTime nowUtc, CancellationToken ct)
    {
        var routeActive = await db.Set<SalesRoute>().AsNoTracking().AnyAsync(r => r.SalesRouteId == routeId && r.TenantId == tenantId && r.IsActive, ct);
        if (!routeActive) return false;
        var hasPrimary = await db.Set<SalesRouteAssignment>().AsNoTracking()
            .AnyAsync(a => a.TenantId == tenantId && a.SalesRouteId == routeId && a.IsActive && a.IsPrimary && a.ValidTo == null, ct);
        if (hasPrimary) return false;
        await OpenPrimaryAsync(db, tenantId, routeId, userId, validFrom, actorUserId, nowUtc, ct);
        return true;
    }

    // ---------------------------------------------------------------- apoyo

    /// <summary>Filas vigentes de la ruta, rastreadas (para cerrarlas). Una anulada siempre lleva ValidTo, así que no aparece.</summary>
    private static Task<List<SalesRouteAssignment>> OpenRowsAsync(TeikemDbContext db, int tenantId, int routeId, CancellationToken ct)
        => db.Set<SalesRouteAssignment>().AsTracking()
            .Where(a => a.TenantId == tenantId && a.SalesRouteId == routeId && a.IsActive && a.ValidTo == null)
            .ToListAsync(ct);

    private static async Task<SalesRouteAssignment> FindAsync(TeikemDbContext db, int tenantId, int assignmentId, CancellationToken ct)
        => await db.Set<SalesRouteAssignment>().AsTracking().FirstOrDefaultAsync(a => a.SalesRouteAssignmentId == assignmentId && a.TenantId == tenantId, ct)
           ?? throw new NotFoundException(SalesRouteRules.AssignmentNotFound, assignmentId, feminine: true);

    private static void Close(SalesRouteAssignment a, DateOnly validTo, int? actorUserId, DateTime nowUtc)
    {
        a.ValidTo = validTo;
        a.ClosedAtUtc = nowUtc;
        a.ClosedBy = actorUserId;
    }

    private static async Task<SalesRouteAssignment> InsertAsync(TeikemDbContext db, int tenantId, int routeId, int userId, bool isPrimary, DateOnly validFrom,
        int? actorUserId, DateTime nowUtc, CancellationToken ct)
    {
        var row = new SalesRouteAssignment
        {
            TenantId = tenantId, SalesRouteId = routeId, UserId = userId, IsPrimary = isPrimary, ValidFrom = validFrom,
            IsActive = true, CreatedAtUtc = nowUtc, CreatedBy = actorUserId,
        };
        db.Set<SalesRouteAssignment>().Add(row);
        await db.SaveGuardedAsync(SalesRouteRules.PeopleChangedConcurrently, ct);
        return row;
    }
}
