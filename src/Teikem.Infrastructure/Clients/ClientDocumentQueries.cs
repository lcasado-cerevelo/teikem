using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Files;
using Teikem.Domain.Tax;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Clients;

/// <summary>
/// Lote 248 — un tipo de documento del cliente para la compañía: id, código, etiqueta combinada (global + la de la compañía, JSON multilingüe), si se puede usar
/// (activo y no apagado por la compañía) y sus marcas (global + CustomExtraJson de la compañía).
/// </summary>
public sealed record ClientDocumentTypeInfo(int LookupCodeId, string Code, string LabelJson, bool IsEnabled, ClientDocumentTypeMarks Marks);

/// <summary>
/// Lote 248 — la exención ACTIVA que respalda un documento: su id, su PublicId, el «hasta» del documento y si sigue vigente (documento activo y «hasta» de hoy en
/// adelante, o sin «hasta»). <see cref="IsCurrent"/> = true es lo que impide quitar el documento (409).
/// </summary>
public sealed record ClientDocumentBackingExemption(int ClientTaxExemptionId, Guid PublicId, DateOnly? ValidTo, bool IsCurrent);

/// <summary>
/// Lote 248 — qué documentos lee <see cref="ClientDocumentQueries.ValidityRowsAsync"/>. Todos los campos son opcionales:
/// - ClientId / DocumentIds: solo ese cliente / esos documentos.
/// - ValidToFrom / ValidToTo: ventana (bordes incluidos) sobre el «hasta»; solo se aplica a los documentos CON «hasta».
/// - IncludeMissing: incluye los documentos SIN «hasta» (de cualquier tipo; quien llama separa MISSING_VALID_TO de NO_EXPIRY con las marcas). False = solo los fechados.
/// </summary>
public sealed record ValidityFilter
{
    public int? ClientId { get; init; }
    public IReadOnlyCollection<int>? DocumentIds { get; init; }
    public DateOnly? ValidToFrom { get; init; }
    public DateOnly? ValidToTo { get; init; }
    public bool IncludeMissing { get; init; }
}

/// <summary>
/// Lote 248 — un documento con lo necesario para su vigencia: cliente, tipo (código, etiqueta combinada y marcas), número, fechas, si está renovado
/// (<see cref="ClientDocumentRules.IsRenewed"/>), la exención activa que respalda (si hay), cuándo se subió y el archivo.
/// </summary>
public sealed record ClientDocumentValidityRow(
    int DocumentId,
    Guid PublicId,
    int ClientId,
    Guid ClientPublicId,
    string ClientCode,
    string ClientName,
    string TypeCode,
    string TypeLabelJson,
    ClientDocumentTypeMarks Marks,
    string? Number,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    bool IsRenewed,
    int? BackingExemptionId,
    DateTime CreatedAtUtc,
    Guid FilePublicId,
    int DocumentTypeLookupId);

/// <summary>
/// Lote 248 — consultas de SOLO LECTURA de los documentos del cliente con vigencia: AsNoTracking y TenantId EXPLÍCITO además del filtro global (las usan también el trabajo
/// diario y el contenido de los avisos, que corren sin usuario). <see cref="ValidityRowsAsync"/> es la ÚNICA definición de «renovado» y de «respalda exención» que
/// comparten la lista «Documentos por vencer», la fuente de datos CLIENT_DOCUMENT y el escaneo de avisos.
/// <para>El catálogo se lee de la base (no de la caché): una marca recién cambiada en Catálogos de valores cuenta de inmediato. <c>lookups</c> se recibe por contrato.</para>
/// </summary>
public static class ClientDocumentQueries
{
    /// <summary>El tipo por código (sin distinguir mayúsculas; el valor propio de la compañía gana sobre el global). NULL = no existe para la compañía.</summary>
    public static async Task<ClientDocumentTypeInfo?> TypeAsync(TeikemDbContext db, ILookupCache lookups, int tenantId, string? code, CancellationToken ct)
    {
        var c = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (c.Length == 0) return null;
        var row = await db.LookupCodes.AsNoTracking()
            .Where(l => l.Entity == ClientDocumentCodes.TypeDomain && l.InternalCode == c && (l.TenantId == null || l.TenantId == tenantId))
            .OrderByDescending(l => l.TenantId.HasValue).ThenBy(l => l.LookupCodeId)
            .Select(l => new { l.LookupCodeId, l.InternalCode, l.LabelJson, l.ExtraJson, l.IsActive })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        var ov = await db.LookupCodeOverrides.AsNoTracking().Where(o => o.TenantId == tenantId && o.LookupCodeId == row.LookupCodeId)
            .Select(o => new { o.IsEnabled, o.CustomLabelJson, o.CustomExtraJson }).FirstOrDefaultAsync(ct);
        return new ClientDocumentTypeInfo(row.LookupCodeId, row.InternalCode, MultilingualText.Merge(row.LabelJson, ov?.CustomLabelJson),
            row.IsActive && ov?.IsEnabled != false, ClientDocumentTypeMarks.Resolve(row.ExtraJson, ov?.CustomExtraJson));
    }

    /// <summary>Los tipos de documento (por id) que ve la compañía, con su etiqueta combinada y sus marcas. <paramref name="typeIds"/> null = todos los del catálogo.</summary>
    public static async Task<Dictionary<int, ClientDocumentTypeInfo>> TypesByIdAsync(TeikemDbContext db, int tenantId, IEnumerable<int>? typeIds, CancellationToken ct)
    {
        var q = db.LookupCodes.AsNoTracking().Where(l => l.Entity == ClientDocumentCodes.TypeDomain && (l.TenantId == null || l.TenantId == tenantId));
        if (typeIds is not null)
        {
            var ids = typeIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, ClientDocumentTypeInfo>();
            q = q.Where(l => ids.Contains(l.LookupCodeId));
        }
        var rows = await q.Select(l => new { l.LookupCodeId, l.InternalCode, l.LabelJson, l.ExtraJson, l.IsActive }).ToListAsync(ct);
        var found = rows.Select(r => r.LookupCodeId).ToList();
        var overrides = found.Count == 0 ? new Dictionary<int, LookupCodeOverride>()
            : (await db.LookupCodeOverrides.AsNoTracking().Where(o => o.TenantId == tenantId && found.Contains(o.LookupCodeId)).ToListAsync(ct))
                .GroupBy(o => o.LookupCodeId).ToDictionary(g => g.Key, g => g.First());
        return rows.ToDictionary(r => r.LookupCodeId, r =>
        {
            var ov = overrides.GetValueOrDefault(r.LookupCodeId);
            return new ClientDocumentTypeInfo(r.LookupCodeId, r.InternalCode, MultilingualText.Merge(r.LabelJson, ov?.CustomLabelJson),
                r.IsActive && ov?.IsEnabled != false, ClientDocumentTypeMarks.Resolve(r.ExtraJson, ov?.CustomExtraJson));
        });
    }

    /// <summary>Las marcas de los tipos (por id). <paramref name="typeIds"/> null = todos los del catálogo. Un tipo que no aparece vale <see cref="ClientDocumentTypeMarks.Default"/>.</summary>
    public static async Task<Dictionary<int, ClientDocumentTypeMarks>> MarksByTypeIdAsync(TeikemDbContext db, int tenantId, IEnumerable<int>? typeIds, CancellationToken ct)
        => (await TypesByIdAsync(db, tenantId, typeIds, ct)).ToDictionary(kv => kv.Key, kv => kv.Value.Marks);

    /// <summary>
    /// La exención ACTIVA que respalda ese documento (a lo más una, por UX_ClientTaxExemption_Document), o NULL. <c>IsCurrent</c> = el documento sigue activo y su «hasta»
    /// es de <paramref name="today"/> en adelante (o no tiene).
    /// </summary>
    public static async Task<ClientDocumentBackingExemption?> ActiveBackingExemptionAsync(TeikemDbContext db, int tenantId, int documentId, DateOnly today, CancellationToken ct)
    {
        var row = await (from e in db.Set<ClientTaxExemption>().AsNoTracking()
                         join d in db.Set<ClientDocument>().AsNoTracking() on e.ClientDocumentId equals d.ClientDocumentId
                         where e.TenantId == tenantId && d.TenantId == tenantId && e.IsActive && e.ClientDocumentId == documentId
                         orderby e.ClientTaxExemptionId
                         select new { e.ClientTaxExemptionId, e.PublicId, d.ValidTo, d.IsActive }).FirstOrDefaultAsync(ct);
        if (row is null) return null;
        return new ClientDocumentBackingExemption(row.ClientTaxExemptionId, row.PublicId, row.ValidTo, row.IsActive && (row.ValidTo is null || row.ValidTo >= today));
    }

    /// <summary>Fila base (con inicializador de miembros, no constructor: así EF puede seguir componiendo Where sobre ella).</summary>
    private sealed class BaseRow
    {
        public int DocumentId { get; init; }
        public Guid PublicId { get; init; }
        public int ClientId { get; init; }
        public Guid ClientPublicId { get; init; }
        public string ClientCode { get; init; } = string.Empty;
        public string ClientName { get; init; } = string.Empty;
        public int TypeId { get; init; }
        public string? Number { get; init; }
        public DateOnly? ValidFrom { get; init; }
        public DateOnly? ValidTo { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public Guid FilePublicId { get; init; }
    }

    /// <summary>Documentos activos, de clientes activos de la compañía, con el archivo activo.</summary>
    private static IQueryable<BaseRow> Base(TeikemDbContext db, int tenantId)
        => from d in db.Set<ClientDocument>().AsNoTracking()
           join c in db.Clients.AsNoTracking() on d.ClientId equals c.ClientId
           join f in db.Set<StoredFile>().AsNoTracking() on d.StoredFileId equals f.StoredFileId
           where d.TenantId == tenantId && c.TenantId == tenantId && f.TenantId == tenantId && d.IsActive && c.IsActive && f.IsActive
           select new BaseRow
           {
               DocumentId = d.ClientDocumentId, PublicId = d.PublicId, ClientId = c.ClientId, ClientPublicId = c.PublicId, ClientCode = c.Code, ClientName = c.Name,
               TypeId = d.DocumentTypeLookupId, Number = d.DocumentNumber, ValidFrom = d.ValidFrom, ValidTo = d.ValidTo, CreatedAtUtc = d.CreatedAtUtc, FilePublicId = f.PublicId,
           };

    /// <summary>
    /// Los documentos que pide el filtro con su vigencia, ordenados por «hasta» (los sin «hasta» al final) y luego por id.
    /// - Renovado: <see cref="ClientDocumentRules.IsRenewed"/> contra los demás documentos activos (archivo activo) del mismo cliente y tipo, estén o no en la ventana.
    /// - BackingExemptionId: la exención ACTIVA vinculada al documento (sin mirar fechas).
    /// </summary>
    public static async Task<IReadOnlyList<ClientDocumentValidityRow>> ValidityRowsAsync(TeikemDbContext db, ILookupCache lookups, int tenantId, ValidityFilter filter,
        CancellationToken ct)
    {
        var q = Base(db, tenantId);
        if (filter.ClientId is int clientId) q = q.Where(r => r.ClientId == clientId);
        if (filter.DocumentIds is not null)
        {
            var ids = filter.DocumentIds.Distinct().ToList();
            if (ids.Count == 0) return Array.Empty<ClientDocumentValidityRow>();
            q = q.Where(r => ids.Contains(r.DocumentId));
        }
        var from = filter.ValidToFrom;
        var to = filter.ValidToTo;
        var missing = filter.IncludeMissing;
        q = q.Where(r => (r.ValidTo != null && (from == null || r.ValidTo >= from) && (to == null || r.ValidTo <= to)) || (missing && r.ValidTo == null));
        var rows = await q.ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<ClientDocumentValidityRow>();

        // Hermanos: todos los documentos activos de esos clientes con los mismos tipos (la ventana no aplica: el que renueva suele vencer después).
        var clientIds = rows.Select(r => r.ClientId).Distinct().ToList();
        var typeIds = rows.Select(r => r.TypeId).Distinct().ToList();
        var siblings = (await Base(db, tenantId).Where(r => clientIds.Contains(r.ClientId) && typeIds.Contains(r.TypeId))
                .Select(r => new { r.DocumentId, r.ClientId, r.TypeId, r.ValidTo }).ToListAsync(ct))
            .Select(s => new ClientDocumentValidityKey(s.DocumentId, s.ClientId, s.TypeId, s.ValidTo))
            .ToLookup(s => (s.ClientId, s.DocumentTypeLookupId));

        var docIds = rows.Select(r => r.DocumentId).ToList();
        var backing = (await db.Set<ClientTaxExemption>().AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.IsActive && e.ClientDocumentId != null && docIds.Contains(e.ClientDocumentId.Value))
                .Select(e => new { DocumentId = e.ClientDocumentId!.Value, e.ClientTaxExemptionId }).ToListAsync(ct))
            .GroupBy(e => e.DocumentId).ToDictionary(g => g.Key, g => g.Min(e => e.ClientTaxExemptionId));

        var types = await TypesByIdAsync(db, tenantId, typeIds, ct);
        return rows
            .Select(r =>
            {
                var type = types.GetValueOrDefault(r.TypeId);
                var key = new ClientDocumentValidityKey(r.DocumentId, r.ClientId, r.TypeId, r.ValidTo);
                return new ClientDocumentValidityRow(r.DocumentId, r.PublicId, r.ClientId, r.ClientPublicId, r.ClientCode, r.ClientName,
                    type?.Code ?? string.Empty, type?.LabelJson ?? "{}", type?.Marks ?? ClientDocumentTypeMarks.Default, r.Number, r.ValidFrom, r.ValidTo,
                    ClientDocumentRules.IsRenewed(key, siblings[(r.ClientId, r.TypeId)]), backing.TryGetValue(r.DocumentId, out var ex) ? ex : null,
                    r.CreatedAtUtc, r.FilePublicId, r.TypeId);
            })
            .OrderBy(r => r.ValidTo is null).ThenBy(r => r.ValidTo).ThenBy(r => r.DocumentId)
            .ToList();
    }
}
