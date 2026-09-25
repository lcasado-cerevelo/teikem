using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 2 (P5): servicios especiales por cliente (componente 5 del modelo de facturación) y tipos compartidos del tenant.
/// - Los tipos (SpecialServiceType) son del tenant, no del cliente: al crear uno desde un cliente queda disponible para
///   todos al instante (R20) y alimentará el selector 'tipo de viaje' de Choferes (R19). Se comparan normalizados
///   (sin mayúsculas/acentos/espacios dobles) para no duplicar variantes; UQ (TenantId, Name) es la segunda barrera.
/// - La tarifa es efectivo-fechada (principio #8): editar = cerrar la fila abierta y abrir otra (R28); quitar = cerrar.
///   Dos ediciones el mismo día dejan una fila de longitud cero que nunca está vigente pero queda como historial.
/// - Alta solo con contrato vigente y el componente encendido (R12, 409); las filas se devuelven aunque el componente
///   esté apagado (R7b: no se pierden). El tipo de una fila es inmutable.
/// - Toda fila se alcanza a través del cliente del tenant (ResolveClientAsync + ClientId), nunca por id suelto.
/// - Escrituras: EnsureAllowedAsync(CONTRACT, contratoVigente, EDIT_CONTRACT) (422 en EXPIRED/CANCELLED por defecto)
///   y RunInTransactionAsync (la estrategia de reintentos limpia el tracker: todo se carga dentro del delegado).
/// </summary>
public sealed class SpecialServiceService(TeikemDbContext db, ITenantContext tenant, StatusService status)
{
    private const string NoContractMessage = "El cliente no tiene un contrato vigente; cree o active un contrato antes de configurar servicios especiales.";
    private const string ComponentOffMessage = "El componente 'Servicios especiales' está apagado en el contrato vigente; enciéndalo en el modelo de facturación para agregar servicios especiales.";
    private const string AlreadyClosedMessage = "La tarifa ya está cerrada; agregue un servicio especial nuevo si necesita volver a cobrarlo.";

    // ---------------------------------------------------------------- tipos del tenant

    /// <summary>Unión de tipos del tenant con cuántos clientes tienen una tarifa abierta de cada uno.</summary>
    public async Task<IReadOnlyList<SpecialServiceTypeDto>> GetTypesAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.SpecialServiceTypes.AsNoTracking();
        if (!includeInactive) q = q.Where(t => t.IsActive);
        var types = await q.OrderBy(t => t.Name).ThenBy(t => t.SpecialServiceTypeId).ToListAsync(ct);
        if (types.Count == 0) return Array.Empty<SpecialServiceTypeDto>();

        // Clientes distintos con fila abierta por tipo (UQ_SpecialService_Open ya garantiza una abierta por cliente y tipo).
        var open = await db.SpecialServices.AsNoTracking()
            .Where(s => s.IsActive && s.EffectiveTo == null)
            .Select(s => new { s.SpecialServiceTypeId, s.ClientId })
            .Distinct()
            .ToListAsync(ct);
        var usage = open.GroupBy(o => o.SpecialServiceTypeId).ToDictionary(g => g.Key, g => g.Count());

        return types.Select(t => new SpecialServiceTypeDto(t.SpecialServiceTypeId, t.Name, usage.GetValueOrDefault(t.SpecialServiceTypeId), t.IsActive)).ToList();
    }

    /// <summary>
    /// Baja lógica de un tipo (nunca DELETE, documento L214): deja de aparecer en el selector de todos los clientes.
    /// Solo si ningún cliente tiene una tarifa abierta de ese tipo (409 en otro caso); las filas cerradas conservan su historial.
    /// </summary>
    public async Task<SpecialServiceTypeDto> DeactivateTypeAsync(int typeId, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var type = await db.SpecialServiceTypes.FirstOrDefaultAsync(t => t.SpecialServiceTypeId == typeId, ct2)
                       ?? throw new NotFoundException("Tipo de servicio especial", typeId);
            if (!type.IsActive) throw new ConflictException("El tipo ya está inactivo.");
            var clientsUsing = await db.SpecialServices.AsNoTracking()
                .Where(s => s.SpecialServiceTypeId == typeId && s.IsActive && s.EffectiveTo == null)
                .Select(s => s.ClientId).Distinct().CountAsync(ct2);
            if (clientsUsing > 0)
                throw new ConflictException($"El tipo tiene tarifas vigentes en {clientsUsing} cliente(s); ciérrelas antes de inactivarlo.");
            type.IsActive = false; // el interceptor audita el cambio
            await db.SaveChangesAsync(ct2);
        }, ct);
        return await GetTypeAsync(typeId, ct);
    }

    /// <summary>Reactivación de un tipo dado de baja: vuelve al selector con su nombre e historial.</summary>
    public async Task<SpecialServiceTypeDto> ReactivateTypeAsync(int typeId, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var type = await db.SpecialServiceTypes.FirstOrDefaultAsync(t => t.SpecialServiceTypeId == typeId, ct2)
                       ?? throw new NotFoundException("Tipo de servicio especial", typeId);
            if (type.IsActive) throw new ConflictException("El tipo ya está activo.");
            type.IsActive = true;
            await db.SaveChangesAsync(ct2);
        }, ct);
        return await GetTypeAsync(typeId, ct);
    }

    private async Task<SpecialServiceTypeDto> GetTypeAsync(int typeId, CancellationToken ct)
        => (await GetTypesAsync(includeInactive: true, ct)).FirstOrDefault(t => t.Id == typeId)
           ?? throw new NotFoundException("Tipo de servicio especial", typeId);

    // ---------------------------------------------------------------- consulta por cliente

    public async Task<SpecialServiceListDto> GetForClientAsync(Guid clientPublicId, DateOnly? asOf, bool includeHistory, CancellationToken ct)
    {
        var client = await db.ResolveClientAsync(clientPublicId, ct); // 404 si no es del tenant
        var date = asOf ?? Today();
        var contract = await db.CurrentContractAsync(client.ClientId, date, ct);

        var clientId = client.ClientId;
        var rows = await db.SpecialServices.AsNoTracking().Include(s => s.Type)
            .Where(s => s.ClientId == clientId && s.IsActive)
            .ToListAsync(ct);

        IEnumerable<SpecialServiceDto> items = rows.Select(r => ToDto(r, date));
        if (!includeHistory) items = items.Where(i => i.IsCurrent);
        var list = items.OrderBy(i => i.TypeName).ThenBy(i => i.EffectiveFrom).ThenBy(i => i.Id).ToList();

        return new SpecialServiceListDto(client.PublicId, contract?.PublicId, contract?.BillSpecialServices ?? false, date, list);
    }

    // ---------------------------------------------------------------- alta

    public async Task<SpecialServiceDto> AddAsync(Guid clientPublicId, SpecialServiceCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var hasTypeId = req.TypeId.HasValue;
        var hasNewName = !string.IsNullOrWhiteSpace(req.NewTypeName);
        if (hasTypeId == hasNewName)
            throw new ValidationException("typeId", "Indique el tipo de servicio especial: un tipo existente (typeId) o el nombre de uno nuevo (newTypeName), no ambos.");
        var newName = hasNewName ? CleanName(req.NewTypeName) : null;
        ValidateRate(req.Rate);
        var effectiveFrom = req.EffectiveFrom ?? Today();

        var id = await db.RunInTransactionAsync(async ct2 =>
        {
            var client = await db.ResolveClientAsync(clientPublicId, ct2);
            ClientQueries.EnsureClientActive(client); // Lote 3 (ajuste A): cliente dado de baja ⇒ 409, sin servicios especiales nuevos
            var contract = await RequireCurrentContractAsync(client.ClientId, ct2);
            if (!contract.BillSpecialServices) throw new ConflictException(ComponentOffMessage);
            await status.EnsureAllowedAsync(EntityTypes.Contract, contract.StatusCodeId, Capabilities.EditContract, ct2);

            var type = hasTypeId
                ? await ResolveTypeAsync(req.TypeId!.Value, ct2)
                : await ResolveOrCreateTypeAsync(tenantId, newName!, ct2);

            var clientId = client.ClientId;
            var typeId = type.SpecialServiceTypeId;
            // Guarda por intervalo: choca con toda fila del mismo tipo que no haya terminado antes de effectiveFrom
            // (EffectiveTo NULL o > effectiveFrom); UQ_SpecialService_Open (solo abiertas) es la segunda barrera.
            if (await db.SpecialServices.AnyAsync(s => s.ClientId == clientId && s.SpecialServiceTypeId == typeId && s.IsActive
                                                       && (s.EffectiveTo == null || s.EffectiveTo > effectiveFrom), ct2))
                throw new ConflictException(OpenRowMessage(type.Name));

            var row = new SpecialService
            {
                TenantId = tenantId,
                ClientId = clientId,
                SpecialServiceTypeId = typeId,
                Rate = req.Rate,
                EffectiveFrom = effectiveFrom,
                EffectiveTo = null,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.SpecialServices.Add(row);
            await db.SaveGuardedAsync(OpenRowMessage(type.Name), ct2);
            return row.SpecialServiceId;
        }, ct);

        return await GetByIdAsync(id, Today(), ct);
    }

    // ---------------------------------------------------------------- nueva versión de la tarifa

    /// <summary>Cierra la fila abierta en EffectiveFrom y abre otra con la tarifa nueva (R28). Nunca se sobrescribe el monto.</summary>
    public async Task<SpecialServiceDto> UpdateRateAsync(Guid clientPublicId, int id, SpecialServiceRateUpdateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        ValidateRate(req.Rate);
        var newFrom = req.EffectiveFrom ?? Today();
        if (newFrom < Today()) throw new ValidationException("effectiveFrom", RateService.PastDateMessage);

        var newId = await db.RunInTransactionAsync(async ct2 =>
        {
            var client = await db.ResolveClientAsync(clientPublicId, ct2);
            var row = await LoadRowAsync(client.ClientId, id, ct2);
            if (row.EffectiveTo is not null) throw new ConflictException(AlreadyClosedMessage);
            var contract = await RequireCurrentContractAsync(client.ClientId, ct2);
            await status.EnsureAllowedAsync(EntityTypes.Contract, contract.StatusCodeId, Capabilities.EditContract, ct2);

            if (newFrom < row.EffectiveFrom)
                throw new ValidationException("effectiveFrom", $"La nueva vigencia no puede ser anterior al inicio de la tarifa actual ({row.EffectiveFrom:yyyy-MM-dd}).");
            EffectiveDated.ValidateNewVersion(row, newFrom);

            // Primero se cierra y se guarda; luego se inserta (UQ_SpecialService_Open solo admite una fila abierta por cliente y tipo).
            EffectiveDated.Close(row, newFrom);
            await db.SaveGuardedAsync(OpenRowMessage(row.Type?.Name ?? ""), ct2);

            var next = new SpecialService
            {
                TenantId = tenantId,
                ClientId = row.ClientId,
                SpecialServiceTypeId = row.SpecialServiceTypeId,
                Rate = req.Rate,
                EffectiveFrom = newFrom,
                EffectiveTo = null,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.SpecialServices.Add(next);
            await db.SaveGuardedAsync(OpenRowMessage(row.Type?.Name ?? ""), ct2);
            return next.SpecialServiceId;
        }, ct);

        return await GetByIdAsync(newId, Today(), ct);
    }

    // ---------------------------------------------------------------- cierre

    /// <summary>Quitar conservando historial: fija EffectiveTo (exclusivo). La fila queda visible con includeHistory.</summary>
    public async Task<SpecialServiceDto> CloseAsync(Guid clientPublicId, int id, SpecialServiceCloseRequest req, CancellationToken ct)
    {
        var effectiveTo = req.EffectiveTo ?? Today();
        if (effectiveTo < Today()) throw new ValidationException("effectiveTo", RateService.PastDateMessage);

        var closedId = await db.RunInTransactionAsync(async ct2 =>
        {
            var client = await db.ResolveClientAsync(clientPublicId, ct2);
            var row = await LoadRowAsync(client.ClientId, id, ct2);
            if (row.EffectiveTo is not null) throw new ConflictException(AlreadyClosedMessage);
            var contract = await RequireCurrentContractAsync(client.ClientId, ct2);
            await status.EnsureAllowedAsync(EntityTypes.Contract, contract.StatusCodeId, Capabilities.EditContract, ct2);

            if (effectiveTo < row.EffectiveFrom)
                throw new ValidationException("effectiveTo", $"La fecha de cierre no puede ser anterior al inicio de la tarifa ({row.EffectiveFrom:yyyy-MM-dd}).");
            EffectiveDated.Close(row, effectiveTo);
            await db.SaveGuardedAsync(OpenRowMessage(row.Type?.Name ?? ""), ct2);
            return row.SpecialServiceId;
        }, ct);

        return await GetByIdAsync(closedId, Today(), ct);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Contrato vigente del cliente hoy (ACTIVE con StartDate &lt;= hoy o, si no, el DRAFT más reciente); sin contrato → 409.</summary>
    private async Task<Contract> RequireCurrentContractAsync(int clientId, CancellationToken ct)
        => await db.CurrentContractAsync(clientId, Today(), ct) ?? throw new ConflictException(NoContractMessage);

    /// <summary>La fila se carga SIEMPRE a través del cliente del tenant (regla tenant-security del lote).</summary>
    private async Task<SpecialService> LoadRowAsync(int clientId, int id, CancellationToken ct)
        => await db.SpecialServices.Include(s => s.Type)
               .FirstOrDefaultAsync(s => s.SpecialServiceId == id && s.ClientId == clientId && s.IsActive, ct)
           ?? throw new NotFoundException("Servicio especial", id);

    private async Task<SpecialServiceType> ResolveTypeAsync(int typeId, CancellationToken ct)
    {
        var type = await db.SpecialServiceTypes.FirstOrDefaultAsync(t => t.SpecialServiceTypeId == typeId, ct)
                   ?? throw new NotFoundException("Tipo de servicio especial", typeId);
        if (!type.IsActive) throw new ValidationException("typeId", "El tipo de servicio especial está inactivo; reactívelo o elija otro.");
        return type;
    }

    /// <summary>
    /// Reutiliza el tipo del tenant cuyo nombre normalizado coincide (evita 'Vagón del muelle' vs 'VAGON DEL MUELLE');
    /// si no existe lo crea (queda disponible para todos los clientes al instante, R20). Un tipo inactivo que coincide se reactiva.
    /// </summary>
    private async Task<SpecialServiceType> ResolveOrCreateTypeAsync(int tenantId, string cleanName, CancellationToken ct)
    {
        var existing = await db.SpecialServiceTypes.ToListAsync(ct);
        var match = existing.FirstOrDefault(t => SpecialServiceRules.SameName(t.Name, cleanName));
        if (match is not null)
        {
            if (!match.IsActive) match.IsActive = true;
            return match;
        }
        var type = new SpecialServiceType { TenantId = tenantId, Name = cleanName, IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        db.SpecialServiceTypes.Add(type);
        await db.SaveGuardedAsync($"Ya existe un tipo de servicio especial con el nombre '{cleanName}'.", ct);
        return type;
    }

    private static string CleanName(string? name)
    {
        try { return SpecialServiceRules.CleanName(name); }
        catch (ArgumentException ex) { throw new ValidationException("newTypeName", ex.Message); }
    }

    private static void ValidateRate(decimal rate)
    {
        if (rate < 0) throw new ValidationException("rate", "La tarifa del servicio especial no puede ser negativa.");
    }

    private static string OpenRowMessage(string typeName)
        => $"El cliente ya tiene una tarifa vigente en esa fecha para el tipo '{typeName}'; edite esa tarifa o ciérrela antes de agregar otra.";

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<SpecialServiceDto> GetByIdAsync(int id, DateOnly asOf, CancellationToken ct)
    {
        var row = await db.SpecialServices.AsNoTracking().Include(s => s.Type).FirstAsync(s => s.SpecialServiceId == id, ct);
        return ToDto(row, asOf);
    }

    private static SpecialServiceDto ToDto(SpecialService r, DateOnly asOf)
        => new(r.SpecialServiceId, r.SpecialServiceTypeId, r.Type?.Name ?? "", r.Rate, r.EffectiveFrom, r.EffectiveTo, EffectiveDated.IsCurrentOn(r, asOf));
}
