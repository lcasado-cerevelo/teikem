using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Notifications;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Lote 248 — lo que hizo una revisión: avisos nuevos de «por vencer» y de «vencido», los que ya se habían avisado y los que quedaron para la siguiente corrida (tope).</summary>
public sealed record DocumentExpiryScanResult(int Expiring, int Expired, int AlreadySent, int Deferred)
{
    public int New => Expiring + Expired;
}

/// <summary>
/// Lote 248 — la revisión diaria de vencimientos (trabajo DOCUMENT_EXPIRY_SCAN), una compañía a la vez y como la compañía (sin usuario):
/// 1. Lee los umbrales y el «aviso diario después de vencido» de dbo.Tenant (o los de omisión si la columna está rota).
/// 2. Por cada fuente (<see cref="IExpiryNoticeSource"/>) con su módulo encendido, pide lo que vence en la ventana [hoy − lookback, hoy + el mayor umbral].
/// 3. Decide el aviso de cada candidato (<see cref="ExpiryNoticeRules.Decide"/>: solo el umbral más cercano) y arma la clave «{prefijo}:{id}:{hasta}:{marca}».
/// 4. Busca la clave en NotificationOutbox para LOS DOS eventos del par (por vencer y vencido): el publicador solo mira el mismo código, y así el umbral 0
///    publicado como «por vencer» no se repite al día siguiente como «vencido».
/// 5. Publica y guarda aviso por aviso. Si otra corrida en paralelo ya lo anotó (índice único UX_NotificationOutbox_Dedup, 2601), se suelta y cuenta como ya avisado.
/// Tope de <see cref="DocumentExpiryCodes.MaxNoticesPerRun"/> avisos nuevos por corrida; lo demás queda para la siguiente (Deferred).
/// El aviso se anota aunque no haya ninguna regla del evento: el despachador lo cierra sin entregas, y crear la regla después no dispara de golpe los umbrales que ya pasaron.
/// </summary>
public class DocumentExpiryScanService(
    TeikemDbContext db, ITenantContext tenant, ITenantClock clock, INotificationPublisher publisher, ModuleService modules, IEnumerable<IExpiryNoticeSource> sources)
{
    /// <summary>Tamaño de cada consulta de claves ya anotadas (lista IN).</summary>
    private const int KeyChunk = 500;

    private sealed record Pending(ExpiryCandidate Candidate, ExpiryNoticeDecision Decision, string EventCode, string Key, IReadOnlyCollection<string> PairCodes);

    public async Task<DocumentExpiryScanResult> ScanAsync(CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var settings = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId)
            .Select(t => new { t.DocExpiryThresholdDays, t.DocExpiryDailyAfterExpiry }).FirstOrDefaultAsync(ct);
        var thresholds = ExpiryNoticeRules.ThresholdsOrDefault(settings?.DocExpiryThresholdDays ?? DocumentExpiryCodes.DefaultThresholds);
        var daily = settings?.DocExpiryDailyAfterExpiry ?? false;
        var today = clock.Today;
        var lookahead = Math.Max(0, thresholds.DefaultIfEmpty(0).Max());

        int expiring = 0, expired = 0, already = 0, deferred = 0;
        foreach (var source in sources)
        {
            if (!await modules.IsEnabledAsync(source.ModuleKey, ct)) continue;
            var lookback = source.NotifyAfterExpiry ? ExpiryNoticeRules.LookbackDays(thresholds, daily) : 0;
            var candidates = await source.CandidatesAsync(today, lookahead, lookback, ct);

            var pending = new List<Pending>();
            foreach (var c in candidates.OrderBy(c => c.ValidTo).ThenBy(c => c.EntityId))
            {
                var allowExpired = source.NotifyAfterExpiry && c.ExpiredEvent is not null;
                var decision = ExpiryNoticeRules.Decide(c.ValidTo, today, thresholds, daily, allowExpired);
                if (decision is null) continue;
                var code = decision.Expired ? c.ExpiredEvent! : c.ExpiringEvent;
                var pair = c.ExpiredEvent is null ? new[] { c.ExpiringEvent } : new[] { c.ExpiringEvent, c.ExpiredEvent };
                pending.Add(new Pending(c, decision, code, ExpiryNoticeRules.DedupKey(source.DedupPrefix, c.EntityId, c.ValidTo, decision.Marker), pair));
            }
            if (pending.Count == 0) continue;

            var sent = await SentKeysAsync(tenantId, pending, ct);
            foreach (var p in pending)
            {
                if (p.PairCodes.Any(code => sent.Contains((code, p.Key)))) { already++; continue; }
                if (expiring + expired >= DocumentExpiryCodes.MaxNoticesPerRun) { deferred++; continue; }
                switch (await PublishOneAsync(p, today, ct))
                {
                    case true when p.Decision.Expired: expired++; break;
                    case true: expiring++; break;
                    default: already++; break;
                }
            }
        }
        return new DocumentExpiryScanResult(expiring, expired, already, deferred);
    }

    /// <summary>Las claves (evento, clave) ya anotadas en NotificationOutbox de la compañía, para los códigos y claves de los candidatos.</summary>
    private async Task<HashSet<(string Code, string Key)>> SentKeysAsync(int tenantId, IReadOnlyList<Pending> pending, CancellationToken ct)
    {
        var codes = pending.SelectMany(p => p.PairCodes).Distinct().ToList();
        var result = new HashSet<(string, string)>();
        foreach (var chunk in pending.Select(p => p.Key).Distinct().Chunk(KeyChunk))
        {
            var keys = chunk.ToList();
            var rows = await db.NotificationOutbox.AsNoTracking()
                .Where(o => o.TenantId == tenantId && codes.Contains(o.EventCode) && o.DedupKey != null && keys.Contains(o.DedupKey))
                .Select(o => new { o.EventCode, o.DedupKey }).ToListAsync(ct);
            foreach (var r in rows) result.Add((r.EventCode, r.DedupKey!));
        }
        return result;
    }

    /// <summary>Publica y guarda un aviso: true = nuevo; false = ya estaba (el publicador lo encontró, o el índice único lo rechazó porque otra corrida lo anotó primero).</summary>
    private async Task<bool> PublishOneAsync(Pending p, DateOnly today, CancellationToken ct)
    {
        var values = new Dictionary<string, string?>(p.Candidate.Values, StringComparer.Ordinal)
        {
            ["days"] = Math.Abs(p.Candidate.ValidTo.DayNumber - today.DayNumber).ToString(CultureInfo.InvariantCulture),
            ["threshold"] = p.Decision.Threshold is int t ? t.ToString(CultureInfo.InvariantCulture) : string.Empty,
        };
        if (!await publisher.PublishAsync(p.EventCode, values, p.Key, ct)) return false;
        try
        {
            await SaveAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicate(ex))
        {
            // otra corrida lo anotó primero: se suelta lo agregado para que no vuelva a fallar en el siguiente guardado
            foreach (var entry in db.ChangeTracker.Entries<NotificationOutbox>().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>Guarda el aviso recién anotado (separado para poder simular en pruebas la carrera con otra corrida).</summary>
    protected virtual Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    /// <summary>¿La falla al guardar es la violación del índice único de la clave de no repetición (SQL Server 2601/2627)?</summary>
    protected virtual bool IsDuplicate(DbUpdateException ex) => DbExtensions.IsUniqueViolation(ex);

    /// <summary>
    /// El resumen de la corrida para Trabajos programados: «{n} aviso(s) nuevo(s): {a} por vencer y {b} vencido(s); {c} ya se habían avisado.», «Nada que avisar.» y, con
    /// el tope, «… {d} quedaron para la siguiente corrida (tope de 500).».
    /// </summary>
    public static string Summary(DocumentExpiryScanResult r)
    {
        if (r.New == 0 && r.AlreadySent == 0 && r.Deferred == 0) return "Nada que avisar.";
        var text = $"{r.New} aviso(s) nuevo(s): {r.Expiring} por vencer y {r.Expired} vencido(s); {r.AlreadySent} ya se habían avisado.";
        if (r.Deferred > 0) text += $" {r.Deferred} quedaron para la siguiente corrida (tope de {DocumentExpiryCodes.MaxNoticesPerRun}).";
        return text;
    }
}
