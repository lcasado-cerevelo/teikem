using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using R = Teikem.Domain.Clients.ClientChainRules;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 245 (Lote B del plan de rutas, cadenas y precios) — cadenas de clientes: lista, ficha, crear, editar (con rowVersion), activar y desactivar (las guardas
/// viven SOLO en <see cref="SetActiveAsync"/>), asignar y quitar clientes, cambiar la cadena de un cliente desde su ficha y el alta masiva por el inicio del nombre
/// (simulación con huella y aplicación). Una cadena NO es una ruta ni cambia la ruta del cliente.
/// <para>
/// El TenantId sale de la sesión (además del filtro global, lo que se crea lo lleva explícito). Todo va sobre DbSets filtrados: una cadena o un cliente de otra
/// compañía da 404 sin decir cuál. Lo que escribe va dentro de RunInTransactionAsync con las lecturas DENTRO del trabajo (al reintentar se limpia el ChangeTracker).
/// </para>
/// <para>
/// Carrera desactivar ↔ asignar: toda asignación «toca» la cadena (UpdatedAtUtc, marcado como modificado aunque el reloj no avance) para que el UPDATE viaje con
/// RowVersion; la desactivación vuelve a contar los clientes activos dentro de su transacción. Gana el primero; el segundo recibe 409 ConcurrencyMessage. El toque no
/// deja bitácora porque UpdatedAtUtc y UpdatedBy son [NotAudited].
/// </para>
/// </summary>
public sealed class ClientChainService(TeikemDbContext db, ITenantContext tenant, ITenantClock clock)
{
    private const string ChainWhat = "Cadena";
    private const string ClientWhat = "Cliente";

    private int TenantId => ((TenantContext)tenant).RequireTenantId();

    private IQueryable<ClientChain> Chains => db.Set<ClientChain>();

    // ================================================================ lectura

    /// <summary>Cadenas de la compañía (por omisión solo las activas), con buscador por nombre. ClientCount = clientes ACTIVOS.</summary>
    public async Task<IReadOnlyList<ClientChainListItemDto>> ListAsync(bool includeInactive, string? q, CancellationToken ct)
    {
        var query = Chains.AsNoTracking();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(x => x.Name.Contains(s));
        }
        var chains = await query.OrderBy(x => x.Name).ThenBy(x => x.ClientChainId).ToListAsync(ct);
        if (chains.Count == 0) return Array.Empty<ClientChainListItemDto>();
        var counts = await ActiveCountsAsync(ct);
        return chains.Select(x => new ClientChainListItemDto(x.PublicId, x.Name, x.Notes, x.IsActive, counts.GetValueOrDefault(x.ClientChainId))).ToList();
    }

    /// <summary>Ficha de la cadena con sus conteos (clientes activos e inactivos) y el RowVersion en base64. 404 «Cadena no encontrada.».</summary>
    public async Task<ClientChainDetailDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var chain = await Chains.AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw ChainNotFound();
        var id = chain.ClientChainId;
        var active = await db.Clients.AsNoTracking().CountAsync(c => c.ChainId == id && c.IsActive, ct);
        var inactive = await db.Clients.AsNoTracking().CountAsync(c => c.ChainId == id && !c.IsActive, ct);
        return new ClientChainDetailDto(chain.PublicId, chain.Name, chain.Notes, chain.IsActive, active, inactive, chain.CreatedAtUtc, chain.UpdatedAtUtc,
            chain.RowVersion is null ? string.Empty : Convert.ToBase64String(chain.RowVersion));
    }

    /// <summary>
    /// Clientes de la cadena (por omisión solo los activos), con buscador por código o nombre, por nombre. take de 1 a 500; <paramref name="all"/> = todos (para
    /// exportar u ordenar la consulta completa). 404 si la cadena no es de la compañía.
    /// </summary>
    public async Task<PagedResult<ClientChainClientDto>> ListClientsAsync(Guid publicId, string? q, bool includeInactive, int skip, int take, bool all, CancellationToken ct)
    {
        var chainId = await Chains.AsNoTracking().Where(x => x.PublicId == publicId).Select(x => (int?)x.ClientChainId).FirstOrDefaultAsync(ct) ?? throw ChainNotFound();
        var query = db.Clients.AsNoTracking().Where(c => c.ChainId == chainId);
        if (!includeInactive) query = query.Where(c => c.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(c => c.Code.Contains(s) || c.Name.Contains(s));
        }
        var total = await query.CountAsync(ct);
        skip = Math.Max(0, skip);
        take = all ? Math.Max(total, 1) : Math.Clamp(take <= 0 ? 50 : take, 1, R.MaxPageSize);
        var ordered = query.OrderBy(c => c.Name).ThenBy(c => c.Code).ThenBy(c => c.ClientId);
        var page = await (all ? ordered : ordered.Skip(skip).Take(take))
            .Select(c => new { c.PublicId, c.Code, c.Name, c.StatusCodeId, c.IsActive, c.SalesRepId })
            .ToListAsync(ct);
        if (all) skip = 0;

        var statusIds = page.Select(c => c.StatusCodeId).Distinct().ToList();
        var statuses = await db.StatusCodes.AsNoTracking().Where(s => statusIds.Contains(s.StatusCodeId))
            .Select(s => new { s.StatusCodeId, s.InternalCode, s.LabelJson }).ToDictionaryAsync(s => s.StatusCodeId, ct);
        var repIds = page.Where(c => c.SalesRepId != null).Select(c => c.SalesRepId!.Value).Distinct().ToList();
        var reps = repIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Set<SalesRep>().AsNoTracking().Where(r => repIds.Contains(r.SalesRepId)).ToDictionaryAsync(r => r.SalesRepId, r => r.Name, ct);

        var items = page.Select(c =>
        {
            var st = statuses.GetValueOrDefault(c.StatusCodeId);
            return new ClientChainClientDto(c.PublicId, c.Code, c.Name, st?.InternalCode ?? string.Empty,
                st is null ? string.Empty : MultilingualText.Resolve(st.LabelJson, tenant.Lang), c.IsActive,
                c.SalesRepId is int rep ? reps.GetValueOrDefault(rep) : null);
        }).ToList();
        return new PagedResult<ClientChainClientDto>(items, total, skip, take);
    }

    // ================================================================ alta, edición y activa

    /// <summary>Crea una cadena: nombre obligatorio (hasta 100, único entre las activas sin distinguir mayúsculas, acentos, puntos ni comas → 409) y notas (hasta 500).</summary>
    public async Task<ClientChainDetailDto> CreateAsync(ClientChainRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        var (name, nameError) = R.NormalizeName(req.Name);
        if (nameError is not null) throw new ValidationException("name", nameError);
        var (notes, notesError) = R.NormalizeNotes(req.Notes);
        if (notesError is not null) throw new ValidationException("notes", notesError);
        var tid = TenantId;
        if (await NameTakenAsync(name!, null, ct)) throw new ConflictException(R.NameTaken);

        var chain = new ClientChain { PublicId = Guid.NewGuid(), TenantId = tid, Name = name!, Notes = notes, IsActive = true, CreatedAtUtc = clock.UtcNow, CreatedBy = tenant.UserId };
        db.Set<ClientChain>().Add(chain);
        await db.SaveGuardedAsync(R.NameTaken, ct);
        return await GetAsync(chain.PublicId, ct);
    }

    /// <summary>
    /// Edita nombre (null = no cambia; renombrar una activa revisa la unicidad → 409) y notas (null = no cambia; ClearNotes las borra). RowVersion opcional: 409
    /// ConcurrencyMessage si la cadena cambió.
    /// </summary>
    public async Task<ClientChainDetailDto> UpdateAsync(Guid publicId, ClientChainRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        string? name = null;
        if (req.Name is not null)
        {
            var (n, e) = R.NormalizeName(req.Name);
            if (e is not null) throw new ValidationException("name", e);
            name = n;
        }
        string? notes = null;
        if (!req.ClearNotes && req.Notes is not null)
        {
            var (n, e) = R.NormalizeNotes(req.Notes);
            if (e is not null) throw new ValidationException("notes", e);
            notes = n;
        }

        await db.RunInTransactionAsync(async c =>
        {
            var chain = await FindTrackedAsync(publicId, c);
            db.ApplyRowVersion(chain, req.RowVersion);
            if (name is not null && name != chain.Name)
            {
                if (chain.IsActive && R.NameKey(name) != R.NameKey(chain.Name) && await NameTakenAsync(name, chain.ClientChainId, c))
                    throw new ConflictException(R.NameTaken);
                chain.Name = name;
            }
            if (req.ClearNotes) chain.Notes = null;
            else if (req.Notes is not null) chain.Notes = notes;
            await db.SaveGuardedAsync(R.NameTaken, c);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>
    /// ÚNICO lugar de las reglas de activar y desactivar (el Lote C agrega aquí «tiene precios u ofertas publicadas» en <see cref="DeactivationBlockersAsync"/>):
    /// - desactivar cuenta los clientes ACTIVOS de la cadena DENTRO de la transacción: si hay → 409 «La cadena tiene N clientes activos…»;
    /// - reactivar con el nombre tomado por otra activa → 409 «Ya hay una cadena activa con ese nombre.»;
    /// - pedir el estado que ya tiene no cambia nada; el cambio viaja con RowVersion (una asignación concurrente da 409).
    /// </summary>
    public async Task<ClientChainDetailDto> SetActiveAsync(Guid publicId, bool active, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async c =>
        {
            var chain = await FindTrackedAsync(publicId, c);
            if (chain.IsActive == active) return;
            if (!active)
            {
                if (await DeactivationBlockersAsync(chain.ClientChainId, c) is string blocker) throw new ConflictException(blocker);
            }
            else if (await NameTakenAsync(chain.Name, chain.ClientChainId, c)) throw new ConflictException(R.NameTaken);
            chain.IsActive = active;
            await db.SaveGuardedAsync(R.NameTaken, c);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Lo que impide desactivar la cadena (null = nada). Hoy: clientes activos. El Lote C agrega aquí los precios y ofertas publicados.</summary>
    private async Task<string?> DeactivationBlockersAsync(int chainId, CancellationToken ct)
    {
        var activeClients = await db.Clients.CountAsync(x => x.ChainId == chainId && x.IsActive, ct);
        return activeClients > 0 ? R.HasActiveClients(activeClients) : null;
    }

    // ================================================================ asignar y quitar clientes

    /// <summary>
    /// Asigna clientes a la cadena (activa → si no, 400). Los que están en otra cadena se saltan y se reportan (SkippedOtherChain), salvo con MoveFromOtherChains;
    /// los que ya están cuentan en AlreadyInChain. 400 sin cuerpo, sin clientes o con más de 2000; 404 «Cliente no encontrado.» si falta alguno (también los de otra
    /// compañía).
    /// </summary>
    public async Task<ClientChainAssignResultDto> AssignAsync(Guid publicId, ClientChainAssignRequest req, CancellationToken ct)
    {
        var ids = ValidateClientList(req is null ? null : req.ClientPublicIds, req is null);
        return await db.RunInTransactionAsync(async c =>
        {
            var chain = await FindTrackedAsync(publicId, c);
            if (!chain.IsActive) throw new ValidationException("chainPublicId", R.ChainInactive);
            var clients = await LoadClientsTrackedAsync(ids, c);

            int assigned = 0, moved = 0, already = 0;
            var skipped = new List<Client>();
            foreach (var client in clients)
            {
                switch (R.Classify(client.ChainId, chain.ClientChainId, req!.MoveFromOtherChains))
                {
                    case R.ActionAssign: client.ChainId = chain.ClientChainId; assigned++; break;
                    case R.ActionMove: client.ChainId = chain.ClientChainId; moved++; break;
                    case R.ActionAlready: already++; break;
                    default: skipped.Add(client); break;
                }
            }
            if (assigned + moved > 0)
            {
                Touch(chain);
                await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, c);
            }
            return new ClientChainAssignResultDto(assigned, moved, already, 0, 0, await SkippedAsync(skipped, c));
        }, ct);
    }

    /// <summary>
    /// Quita clientes de la cadena: solo los que están en ESTA cadena; los demás cuentan en NotInChain (idempotente). No exige cadena activa. 400 sin cuerpo, sin
    /// clientes o con más de 2000; 404 si falta alguno.
    /// </summary>
    public async Task<ClientChainAssignResultDto> RemoveAsync(Guid publicId, ClientChainRemoveRequest req, CancellationToken ct)
    {
        var ids = ValidateClientList(req is null ? null : req.ClientPublicIds, req is null);
        return await db.RunInTransactionAsync(async c =>
        {
            var chain = await FindTrackedAsync(publicId, c);
            var clients = await LoadClientsTrackedAsync(ids, c);
            int removed = 0, notInChain = 0;
            foreach (var client in clients)
            {
                if (client.ChainId == chain.ClientChainId) { client.ChainId = null; removed++; }
                else notInChain++;
            }
            if (removed > 0)
            {
                Touch(chain);
                await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, c);
            }
            return new ClientChainAssignResultDto(0, 0, 0, removed, notInChain, Array.Empty<ClientChainSkippedDto>());
        }, ct);
    }

    /// <summary>
    /// Cambia la cadena de un cliente desde su ficha (cambio deliberado: puede mover entre cadenas en un solo paso; ChainPublicId null = quitarlo). 404 si el cliente
    /// o la cadena no son de la compañía; 400 si la cadena destino está desactivada. Toca la cadena de origen y la de destino.
    /// </summary>
    public async Task<ClientChainMembershipDto> SetClientChainAsync(Guid clientPublicId, ClientChainSetRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        await db.RunInTransactionAsync(async c =>
        {
            var client = await db.Clients.AsTracking().FirstOrDefaultAsync(x => x.PublicId == clientPublicId, c) ?? throw new NotFoundException(ClientWhat);
            ClientChain? target = null;
            if (req.ChainPublicId is Guid chainPublicId)
            {
                target = await FindTrackedAsync(chainPublicId, c);
                if (client.ChainId == target.ClientChainId) return;   // ya está en ella: nada que cambiar
                if (!target.IsActive) throw new ValidationException("chainPublicId", R.ChainInactive);
            }
            else if (client.ChainId is null) return;   // ya está sin cadena

            if (client.ChainId is int originId)
            {
                var origin = await Chains.AsTracking().FirstOrDefaultAsync(x => x.ClientChainId == originId, c);
                if (origin is not null) Touch(origin);
            }
            client.ChainId = target?.ClientChainId;
            if (target is not null) Touch(target);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, c);
        }, ct);

        var row = await db.Clients.AsNoTracking().Where(x => x.PublicId == clientPublicId).Select(x => new { x.PublicId, x.ChainId }).FirstAsync(ct);
        if (row.ChainId is not int chainId) return new ClientChainMembershipDto(row.PublicId, null, null, false);
        var chains = await ClientChainQueries.ChainsByIdAsync(db, new[] { chainId }, ct);
        var ch = chains.GetValueOrDefault(chainId);
        return new ClientChainMembershipDto(row.PublicId, ch?.PublicId, ch?.Name, ch?.IsActive ?? false);
    }

    // ================================================================ alta masiva por el inicio del nombre

    /// <summary>
    /// Simulación del alta masiva (NO escribe nada): los clientes ACTIVOS cuyo nombre empieza con el prefijo (comparados en memoria con <see cref="R.MatchesPrefix"/>,
    /// sin depender de la intercalación de SQL Server), su acción (ASSIGN, MOVE, ALREADY, SKIP_OTHER_CHAIN o EXCLUDED para los excluidos), los conteos, TooMany
    /// (más de 2000) y la huella que pide aplicar. Rows: a lo sumo 2000, por nombre. 400 si la cadena está desactivada, si el prefijo no sirve o si un excluido no
    /// está entre los encontrados.
    /// </summary>
    public async Task<ClientChainBulkPreviewDto> PreviewBulkAsync(Guid publicId, ClientChainBulkRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        var prefixKey = RequirePrefix(req.Prefix);
        var chain = await Chains.AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw ChainNotFound();
        if (!chain.IsActive) throw new ValidationException("chainPublicId", R.ChainInactive);
        var plan = await PlanAsync(chain.ClientChainId, prefixKey, req, ct);

        var chainIds = plan.Matched.Where(m => m.ChainId != null).Select(m => m.ChainId!.Value).Distinct().ToList();
        var chains = await ClientChainQueries.ChainsByIdAsync(db, chainIds, ct);
        var rows = plan.Matched
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(m => m.Code, StringComparer.Ordinal).ThenBy(m => m.ClientId)
            .Take(R.MaxBulk)
            .Select(m =>
            {
                var current = m.ChainId is int cid ? chains.GetValueOrDefault(cid) : null;
                return new ClientChainBulkRowDto(m.PublicId, m.Code, m.Name, plan.ActionOf(m), current?.PublicId, current?.Name);
            })
            .ToList();
        return new ClientChainBulkPreviewDto(prefixKey, req.WholeWord, req.MoveFromOtherChains, plan.Matched.Count, plan.Count(R.ActionAssign), plan.Count(R.ActionMove),
            plan.Count(R.ActionAlready), plan.Count(R.ActionSkipOtherChain), plan.Count(R.ActionExcluded), plan.Matched.Count > R.MaxBulk, R.MaxBulk, plan.Fingerprint, rows);
    }

    /// <summary>
    /// Aplica el alta masiva: exige la huella de la simulación (400 sin ella) y la recalcula dentro de la transacción (409 si los clientes que coinciden o su cadena
    /// cambiaron, sin escribir nada); más de 2000 → 400; un excluido fuera del conjunto → 400. Asigna los ASSIGN y mueve los MOVE (solo con MoveFromOtherChains);
    /// salta los de otra cadena y los excluidos. Idempotente: volver a simular y aplicar deja Assigned = Moved = 0.
    /// </summary>
    public async Task<ClientChainBulkResultDto> ApplyBulkAsync(Guid publicId, ClientChainBulkRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        var prefixKey = RequirePrefix(req.Prefix);
        if (string.IsNullOrWhiteSpace(req.Fingerprint)) throw new ValidationException("fingerprint", R.FingerprintRequired);
        var expected = req.Fingerprint.Trim();

        return await db.RunInTransactionAsync(async c =>
        {
            var chain = await FindTrackedAsync(publicId, c);
            if (!chain.IsActive) throw new ValidationException("chainPublicId", R.ChainInactive);
            var plan = await PlanAsync(chain.ClientChainId, prefixKey, req, c, validateExclusions: false);
            if (!string.Equals(plan.Fingerprint, expected, StringComparison.OrdinalIgnoreCase)) throw new ConflictException(R.FingerprintStale);
            if (plan.Matched.Count > R.MaxBulk) throw new ValidationException("prefix", R.BulkTooMany(plan.Matched.Count));
            if (plan.ExclusionError is not null) throw new ValidationException("excludeClientPublicIds", plan.ExclusionError);

            var toChange = plan.Matched.Where(m => plan.ActionOf(m) is R.ActionAssign or R.ActionMove).ToDictionary(m => m.ClientId);
            int assigned = 0, moved = 0;
            if (toChange.Count > 0)
            {
                var ids = toChange.Keys.ToList();
                var tracked = await db.Clients.AsTracking().Where(x => ids.Contains(x.ClientId)).ToListAsync(c);
                foreach (var client in tracked)
                {
                    var m = toChange[client.ClientId];
                    // cambió entre la lectura de la simulación y esta (otra transacción): mejor volver a simular que pisar
                    if (client.ChainId != m.ChainId || !client.IsActive) throw new ConflictException(R.FingerprintStale);
                    if (m.ChainId is null) assigned++; else moved++;
                    client.ChainId = chain.ClientChainId;
                }
                if (tracked.Count != ids.Count) throw new ConflictException(R.FingerprintStale);
                Touch(chain);
                await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, c);
            }
            return new ClientChainBulkResultDto(assigned, moved, plan.Count(R.ActionAlready), plan.Count(R.ActionSkipOtherChain), plan.Count(R.ActionExcluded));
        }, ct);
    }

    /// <summary>Un cliente que coincide con el prefijo, con la cadena en la que está hoy.</summary>
    private sealed record BulkMatch(int ClientId, Guid PublicId, string Code, string Name, int? ChainId);

    /// <summary>El conjunto de una simulación: los que coinciden, su acción (con los excluidos marcados), la huella y el error de exclusión (si lo hay).</summary>
    private sealed class BulkPlan(List<BulkMatch> matched, Dictionary<int, string> actions, string fingerprint, string? exclusionError)
    {
        public List<BulkMatch> Matched { get; } = matched;
        public string Fingerprint { get; } = fingerprint;
        public string? ExclusionError { get; } = exclusionError;
        public string ActionOf(BulkMatch m) => actions[m.ClientId];
        public int Count(string action) => actions.Values.Count(a => a == action);
    }

    private async Task<BulkPlan> PlanAsync(int chainId, string prefixKey, ClientChainBulkRequest req, CancellationToken ct, bool validateExclusions = true)
    {
        var candidates = await db.Clients.AsNoTracking().Where(x => x.IsActive)
            .Select(x => new BulkMatch(x.ClientId, x.PublicId, x.Code, x.Name, x.ChainId)).ToListAsync(ct);
        var matched = candidates.Where(x => R.MatchesPrefix(R.NameKey(x.Name), prefixKey, req.WholeWord)).ToList();

        // exclusiones: por PublicId, deben estar entre los encontrados (no entran en la huella)
        var excludedPublic = (req.ExcludeClientPublicIds ?? Array.Empty<Guid>()).Distinct().ToList();
        var byPublic = matched.ToDictionary(m => m.PublicId);
        var excludedIds = new HashSet<int>();
        string? exclusionError = null;
        foreach (var p in excludedPublic)
        {
            if (byPublic.TryGetValue(p, out var m)) excludedIds.Add(m.ClientId);
            else exclusionError = R.ExclusionOutside;
        }
        exclusionError ??= R.ValidateExclusions(matched.Select(m => m.ClientId).ToHashSet(), excludedIds);
        if (validateExclusions && exclusionError is not null) throw new ValidationException("excludeClientPublicIds", exclusionError);

        var actions = matched.ToDictionary(m => m.ClientId,
            m => excludedIds.Contains(m.ClientId) ? R.ActionExcluded : R.Classify(m.ChainId, chainId, req.MoveFromOtherChains));
        var fingerprint = R.Fingerprint(chainId, prefixKey, req.WholeWord, req.MoveFromOtherChains, matched.Select(m => (m.ClientId, m.ChainId)));
        return new BulkPlan(matched, actions, fingerprint, exclusionError);
    }

    private static string RequirePrefix(string? prefix)
    {
        var (key, error) = R.PrefixKey(prefix);
        if (error is not null) throw new ValidationException("prefix", error);
        return key!;
    }

    // ================================================================ apoyo

    private static NotFoundException ChainNotFound() => new(ChainWhat, feminine: true);

    /// <summary>La cadena, rastreada (sus cambios viajan con RowVersion). 404 «Cadena no encontrada.» si no es de la compañía.</summary>
    private async Task<ClientChain> FindTrackedAsync(Guid publicId, CancellationToken ct)
        => await Chains.AsTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw ChainNotFound();

    /// <summary>
    /// «Toque» optimista: cambia UpdatedAtUtc y lo marca modificado aunque el reloj no avance, para que el UPDATE de la cadena viaje con su RowVersion (una
    /// desactivación concurrente pierde con 409). No deja bitácora: UpdatedAtUtc y UpdatedBy son [NotAudited].
    /// </summary>
    private void Touch(ClientChain chain)
    {
        chain.UpdatedAtUtc = clock.UtcNow;
        chain.UpdatedBy = tenant.UserId;
        db.Entry(chain).Property(x => x.UpdatedAtUtc).IsModified = true;
    }

    /// <summary>¿Otra cadena ACTIVA de la compañía tiene el mismo nombre (sin mayúsculas, acentos, puntos ni comas)?</summary>
    private async Task<bool> NameTakenAsync(string name, int? exceptId, CancellationToken ct)
    {
        var key = R.NameKey(name);
        var names = await Chains.AsNoTracking().Where(x => x.IsActive && (exceptId == null || x.ClientChainId != exceptId)).Select(x => x.Name).ToListAsync(ct);
        return names.Any(n => R.NameKey(n) == key);
    }

    /// <summary>Clientes ACTIVOS por cadena (todas las de la compañía).</summary>
    private async Task<Dictionary<int, int>> ActiveCountsAsync(CancellationToken ct)
        => await db.Clients.AsNoTracking().Where(c => c.IsActive && c.ChainId != null)
            .GroupBy(c => c.ChainId!.Value)
            .Select(g => new { ChainId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ChainId, x => x.Count, ct);

    /// <summary>400 sin cuerpo, sin clientes o con más de 2000; devuelve los PublicId sin repetir.</summary>
    private static List<Guid> ValidateClientList(IReadOnlyList<Guid>? publicIds, bool bodyMissing)
    {
        if (bodyMissing) throw new ValidationException("body", R.BodyRequired);
        if (publicIds is not { Count: > 0 }) throw new ValidationException("clientPublicIds", R.ClientsRequired);
        if (publicIds.Count > R.MaxBulk) throw new ValidationException("clientPublicIds", R.TooManyInCall);
        return publicIds.Distinct().ToList();
    }

    /// <summary>Los clientes por PublicId, rastreados; 404 «Cliente no encontrado.» si falta alguno (también los de otra compañía).</summary>
    private async Task<List<Client>> LoadClientsTrackedAsync(List<Guid> publicIds, CancellationToken ct)
    {
        var clients = await db.Clients.AsTracking().Where(x => publicIds.Contains(x.PublicId)).ToListAsync(ct);
        if (clients.Count != publicIds.Count) throw new NotFoundException(ClientWhat);
        return clients;
    }

    private async Task<IReadOnlyList<ClientChainSkippedDto>> SkippedAsync(List<Client> skipped, CancellationToken ct)
    {
        if (skipped.Count == 0) return Array.Empty<ClientChainSkippedDto>();
        var chains = await ClientChainQueries.ChainsByIdAsync(db, skipped.Select(s => s.ChainId!.Value), ct);
        return skipped.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.ClientId)
            .Select(s =>
            {
                var ch = chains.GetValueOrDefault(s.ChainId!.Value);
                return new ClientChainSkippedDto(s.PublicId, s.Code, s.Name, ch?.PublicId ?? Guid.Empty, ch?.Name ?? string.Empty);
            }).ToList();
    }
}
