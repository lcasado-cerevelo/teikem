using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 4 (P6) — Política de pago a choferes de la compañía (DriverPayPolicy, una fila por tenant, PK TenantId).
/// - Sin fila rigen los valores por defecto (2 niveles de intento, 'Entrega + cada intento'); leerlos no escribe nada.
///   La fila se crea con el primer cambio (upsert).
/// - '+ Agregar intento' solo sube AttemptLevels (tope 20); no crea filas de tarifa: un nivel sin fila se ve 'sin tarifa',
///   distinguible de un $0 capturado a propósito. En este lote no se quitan niveles.
/// - La vista previa corre el motor puro (DriverPayoutRules.Compute) con las tarifas que manda la pantalla; solo lee de BD
///   la fórmula vigente cuando no se indica una.
/// - Escrituras con la fila bloqueada (UPDLOCK, HOLDLOCK): dos '+ Agregar intento' simultáneos no pierden un nivel y dos
///   primeras escrituras no chocan en la PK.
/// </summary>
public sealed class DriverPayPolicyService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    private const string MaxLevelsMessage = "Se alcanzó el máximo de 20 niveles de intento.";

    public async Task<DriverPayPolicyDto> GetAsync(CancellationToken ct)
    {
        var tenantId = db.CurrentTenantId;
        var row = await db.DriverPayPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);
        var formulaCode = row is null
            ? DriverPayoutRules.DefaultFormula
            : (await lookups.GetAsync(row.PayoutFormulaLookupId, ct))?.InternalCode ?? DriverPayoutRules.DefaultFormula;
        var options = await FormulaOptionsAsync(ct);
        var label = options.FirstOrDefault(o => o.Code == formulaCode)?.Label ?? formulaCode;
        return new DriverPayPolicyDto(row?.AttemptLevels ?? DriverPayoutRules.DefaultAttemptLevels, formulaCode, label, options,
            row is null, row?.UpdatedAtUtc);
    }

    /// <summary>Cambia la fórmula vigente (catálogo DriverPayoutFormula). payoutFormula null = sin cambio.</summary>
    public async Task<DriverPayPolicyDto> UpdateAsync(DriverPayPolicyPatchRequest req, CancellationToken ct)
    {
        if (req.PayoutFormula is null) return await GetAsync(ct);
        var code = req.PayoutFormula.Trim().ToUpperInvariant();
        var formulaId = DriverPayoutRules.IsKnownFormula(code)
            ? await lookups.TryGetIdAsync(LookupDomains.DriverPayoutFormula, code, ct)
            : null;
        if (formulaId is null)
            throw new ValidationException("payoutFormula", $"Fórmula de pago desconocida: '{req.PayoutFormula.Trim()}'.");

        await db.RunInTransactionAsync(async ct2 =>
        {
            var row = await LoadForWriteAsync(ct2);
            row.PayoutFormulaLookupId = formulaId.Value;
            Stamp(row);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(ct);
    }

    /// <summary>'+ Agregar intento': AttemptLevels + 1 hasta 20 (409 al tope). No crea filas de tarifa.</summary>
    public async Task<DriverPayPolicyDto> AddAttemptLevelAsync(CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var row = await LoadForWriteAsync(ct2);
            if (row.AttemptLevels >= DriverPayoutRules.MaxAttemptLevels) throw new ConflictException(MaxLevelsMessage);
            row.AttemptLevels += 1;
            Stamp(row);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(ct);
    }

    /// <summary>
    /// Vista previa del pago de una entrega con sus intentos. Usa las tarifas que llegan en la solicitud (no las de un
    /// chofer) y la fórmula indicada o, si no se indica, la vigente de la compañía.
    /// </summary>
    public async Task<PayoutPreviewDto> PreviewAsync(PayoutPreviewRequest req, CancellationToken ct)
    {
        var attempts = (req.Attempts ?? Array.Empty<PayoutAttemptInput>()).Select(a => new PayoutAttempt(a.Number, a.Delivered)).ToList();
        var attemptsError = DriverPayoutRules.ValidateAttempts(attempts);
        if (attemptsError is not null) throw new ValidationException("attempts", attemptsError);

        if (req.DeliveryRate is { } dr) ValidateRate("deliveryRate", dr);
        var rates = new Dictionary<int, decimal>();
        foreach (var r in req.AttemptRates ?? Array.Empty<PayoutAttemptRateInput>())
        {
            var levelError = DriverPayoutRules.ValidateAttemptNumber(r.Number, DriverPayoutRules.MaxAttemptLevels);
            if (levelError is not null) throw new ValidationException("attemptRates", levelError);
            if (!rates.TryAdd(r.Number, r.Rate))
                throw new ValidationException("attemptRates", $"La tarifa del intento {r.Number} está repetida.");
            ValidateRate("attemptRates", r.Rate);
        }

        string formula;
        if (string.IsNullOrWhiteSpace(req.Formula))
            formula = (await SnapshotAsync(db, lookups, ct)).FormulaCode;
        else
        {
            formula = req.Formula.Trim().ToUpperInvariant();
            if (!DriverPayoutRules.IsKnownFormula(formula))
                throw new ValidationException("formula", $"Fórmula de pago desconocida: '{req.Formula.Trim()}'.");
        }

        var result = DriverPayoutRules.Compute(formula, req.DeliveryRate, rates, attempts);
        return new PayoutPreviewDto(formula, result.Total,
            result.Lines.Select(l => new PayoutLineDto(l.Kind, l.AttemptNumber, l.Amount, l.Note)).ToList());
    }

    // ---------------- Compartido con DriverRateService y DriverRateResolver ----------------

    /// <summary>Política vigente de la compañía activa (valores por defecto si no hay fila). Solo lectura.</summary>
    internal static async Task<DriverPayPolicySnapshot> SnapshotAsync(TeikemDbContext db, ILookupCache lookups, CancellationToken ct)
    {
        var tenantId = db.CurrentTenantId; // además del filtro global: una sola fila por compañía (PK TenantId)
        var row = await db.DriverPayPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);
        if (row is null) return new DriverPayPolicySnapshot(DriverPayoutRules.DefaultAttemptLevels, DriverPayoutRules.DefaultFormula);
        var code = (await lookups.GetAsync(row.PayoutFormulaLookupId, ct))?.InternalCode ?? DriverPayoutRules.DefaultFormula;
        return new DriverPayPolicySnapshot(row.AttemptLevels, code);
    }

    // ---------------- Helpers ----------------

    /// <summary>
    /// Carga la fila de la compañía bloqueada (UPDLOCK, HOLDLOCK: también bloquea el hueco si aún no existe) o la crea con
    /// los valores por defecto. Debe correr dentro de RunInTransactionAsync.
    /// </summary>
    private async Task<DriverPayPolicy> LoadForWriteAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("No hay tenant activo en la sesión.");
        var row = await db.DriverPayPolicies
            .FromSqlInterpolated($"SELECT * FROM dbo.DriverPayPolicy WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {tenantId}")
            .FirstOrDefaultAsync(ct);
        if (row is not null) return row;

        row = new DriverPayPolicy
        {
            TenantId = tenantId,
            AttemptLevels = DriverPayoutRules.DefaultAttemptLevels,
            PayoutFormulaLookupId = await lookups.GetIdAsync(LookupDomains.DriverPayoutFormula, DriverPayoutRules.DefaultFormula, ct),
        };
        db.DriverPayPolicies.Add(row);
        return row;
    }

    private void Stamp(DriverPayPolicy row)
    {
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedBy = tenant.UserId;
    }

    private async Task<IReadOnlyList<PayoutFormulaOptionDto>> FormulaOptionsAsync(CancellationToken ct)
        => (await lookups.GetDomainAsync(LookupDomains.DriverPayoutFormula, ct))
            .Where(l => l.IsActive && DriverPayoutRules.IsKnownFormula(l.InternalCode))
            .Select(l => new PayoutFormulaOptionDto(l.InternalCode, MultilingualText.Resolve(l.LabelJson, tenant.Lang)))
            .ToList();

    private static void ValidateRate(string field, decimal rate)
    {
        if (rate < 0) throw new ValidationException(field, "La tarifa no puede ser negativa.");
        var error = FleetRules.DecimalError(rate, 18, 4);
        if (error is not null) throw new ValidationException(field, error);
    }
}
