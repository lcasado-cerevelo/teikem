using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Lote 113 — «Devolver tras N intentos»: el ajuste por compañía y la evaluación de una orden (intentos fallidos vigentes y si toca sugerir devolverla).</summary>
public sealed class ReturnSuggestionService(TeikemDbContext db, ITenantContext tenant)
{
    private int Tid => ((TenantContext)tenant).RequireTenantId();

    public async Task<ReturnSettingsDto> GetSettingsAsync(CancellationToken ct)
        => new(await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == Tid).Select(t => t.ReturnSuggestAfterAttempts).FirstOrDefaultAsync(ct));

    /// <summary>0 = sin sugerencia; de 1 a 20 = con cuántos intentos fallidos se sugiere devolver. 400 fuera de ese rango.</summary>
    public async Task<ReturnSettingsDto> SetSettingsAsync(ReturnSettingsRequest? req, CancellationToken ct)
    {
        if (req?.SuggestAfterAttempts is not int n || !ReturnSuggestionRules.IsValid(n)) throw new ValidationException("suggestAfterAttempts", ReturnSuggestionRules.InvalidMessage);
        var row = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.TenantId == Tid, ct);
        row.ReturnSuggestAfterAttempts = n;
        await db.SaveChangesAsync(ct);
        return new ReturnSettingsDto(n);
    }

    /// <summary>Intentos fallidos vigentes de la orden y si, con el ajuste de su compañía y su estatus, toca sugerir devolverla.</summary>
    public static async Task<(int Attempts, bool Suggest)> EvaluateAsync(TeikemDbContext db, int tenantId, int orderId, string? statusCode, CancellationToken ct)
    {
        var threshold = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Select(t => t.ReturnSuggestAfterAttempts).FirstOrDefaultAsync(ct);
        if (threshold <= 0) return (0, false);
        var attempts = await db.Set<DeliveryAttempt>().AsNoTracking().CountAsync(a => a.TransportOrderId == orderId && a.IsActive && !a.Delivered, ct);
        return (attempts, ReturnSuggestionRules.Suggest(threshold, attempts, statusCode));
    }
}
