using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Security;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Reglas puras de los destinos de aviso (2026-10-23): del CSV que arma el lector (<see cref="NoticeColumns"/>) a los avisos que hay que crear. Una cantidad como «400bx» o «1,430» se lee por su número;
/// una fecha, como MM/dd/yyyy (la de los documentos de Puerto Rico) o yyyy-MM-dd.
/// </summary>
public static class NoticeRowRules
{
    public sealed record Row(int RowNumber, string Reference, string Sku, decimal? Quantity, string? Destination, DateOnly? Date, string? Lot);

    /// <summary>Un aviso por (referencia, destino, fecha); las líneas con el mismo producto se suman.</summary>
    public sealed record Group(string Reference, string? Destination, DateOnly? Date, IReadOnlyList<Row> Rows);

    public static string QuantityInvalid(int row, string raw) => $"Fila {row}: la cantidad «{raw}» no es un número mayor que cero.";
    public static string SkuMissing(int row) => $"Fila {row}: falta el número de ítem (SKU).";
    public static string SkuUnknown(string sku) => $"El producto «{sku}» no existe entre los productos activos de este cliente.";
    public const string NoRows = "El documento no trae ninguna línea con producto y cantidad.";

    public static decimal? ParseQuantity(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var m = Regex.Match(raw, @"\d[\d,]*(\.\d+)?");
        if (!m.Success) return null;
        return decimal.TryParse(m.Value.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out var q) && q > 0 ? q : null;
    }

    public static DateOnly? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        return DateOnly.TryParseExact(text, new[] { "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd", "MM-dd-yyyy", "M/d/yy", "MM/dd/yy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    public static (IReadOnlyList<Group> Groups, IReadOnlyList<(int Row, string Reason)> Errors) BuildGroups(IEnumerable<CsvRow> csv, bool withDestination)
    {
        var rows = new List<Row>();
        var errors = new List<(int, string)>();
        foreach (var r in csv)
        {
            string Cell(int position) => position >= 1 && position <= r.Cells.Count ? r.Cells[position - 1].Trim() : string.Empty;
            var sku = Cell(NoticeColumns.Sku);
            var rawQty = Cell(NoticeColumns.Quantity);
            if (sku.Length == 0 && rawQty.Length == 0) continue;   // renglón vacío
            if (sku.Length == 0) { errors.Add((r.RowNumber, SkuMissing(r.RowNumber))); continue; }
            var qty = ParseQuantity(rawQty);
            if (qty is null) { errors.Add((r.RowNumber, QuantityInvalid(r.RowNumber, rawQty))); continue; }
            var destination = withDestination ? Cell(NoticeColumns.Destination) : string.Empty;
            var lot = Cell(NoticeColumns.Lot);
            rows.Add(new Row(r.RowNumber, Cell(NoticeColumns.Reference), sku, qty, destination.Length == 0 ? null : destination, ParseDate(Cell(NoticeColumns.Date)), lot.Length == 0 ? null : lot));
        }
        var groups = rows.GroupBy(x => (x.Reference.ToUpperInvariant(), (x.Destination ?? string.Empty).ToUpperInvariant(), x.Date))
            .Select(g => new Group(g.First().Reference, g.First().Destination, g.Key.Date, g.ToList())).ToList();
        return (groups, errors);
    }
}

/// <summary>Base de los destinos de aviso: lee el CSV, resuelve los productos del cliente por su SKU y crea un aviso por grupo, en nombre de la cuenta de servicio. Lo que no se puede crear va a la bandeja con el motivo.</summary>
public abstract class NoticeDocumentTarget(TeikemDbContext db, ITenantContext tenant) : IDocumentTarget
{
    public abstract string Kind { get; }
    protected abstract bool WithDestination { get; }
    protected abstract string Label { get; }
    protected abstract Task<Guid> CreateAsync(Guid clientPublicId, Guid? warehousePublicId, NoticeRowRules.Group group, IReadOnlyList<(Guid ProductPublicId, decimal Quantity, string? Lot)> lines, CancellationToken ct);

    public async Task<FileOutcome> ProcessAsync(DocumentTargetRequest request, CancellationToken ct)
    {
        var sha = Convert.ToHexString(SHA256.HashData(request.Content)).ToLowerInvariant();
        var log = new IntegrationFileLog
        {
            TenantId = request.Integration.TenantId, IntegrationId = request.Integration.IntegrationId, IntegrationRunId = request.Run.IntegrationRunId,
            FileName = request.FileName.Length > 260 ? request.FileName[..260] : request.FileName, Sha256 = sha, SizeBytes = request.Content.Length,
        };
        if (request.CheckDuplicate && await db.IntegrationFileLogs.AsNoTracking().AnyAsync(f => f.IntegrationId == request.Integration.IntegrationId && f.Sha256 == sha && f.Result == IntegrationFileResults.Processed, ct))
        {
            log.Result = IntegrationFileResults.Duplicate;
            log.Message = IntegrationRunner.DuplicateFileMessage;
            return new FileOutcome(log, Array.Empty<Guid>(), Array.Empty<RejectedOrder>());
        }
        var created = new List<Guid>();
        var rejected = new List<RejectedOrder>();
        var content = IntegrationRunner.Decode(request.Content);
        var csv = CsvParser.Parse(content, ImportTemplateService.DelimiterOf(request.Template), request.Template.HasHeader);
        var (groups, errors) = NoticeRowRules.BuildGroups(csv, WithDestination);
        foreach (var (row, reason) in errors) rejected.Add(new RejectedOrder(null, row, reason, null));
        if (errors.Count > 0) groups = Array.Empty<NoticeRowRules.Group>();   // si una línea del documento no se pudo leer, NO se crea ningún aviso: uno a medias sería peor que ninguno
        if (groups.Count == 0 && rejected.Count == 0) rejected.Add(new RejectedOrder(null, null, NoticeRowRules.NoRows, null));
        using var asService = ((TenantContext)tenant).As(request.Integration.TenantId, request.Integration.ServiceUserId);
        var skus = groups.SelectMany(g => g.Rows).Select(r => r.Sku.ToUpperInvariant()).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => p.ClientId == request.Client.ClientId && p.IsActive && skus.Contains(p.Sku.ToUpper())).ToListAsync(ct);
        var bySku = products.ToDictionary(p => p.Sku.ToUpperInvariant(), p => p.PublicId);
        foreach (var g in groups)
        {
            ct.ThrowIfCancellationRequested();
            var missing = g.Rows.Where(r => !bySku.ContainsKey(r.Sku.ToUpperInvariant())).ToList();
            foreach (var r in missing) rejected.Add(new RejectedOrder(g.Reference, r.RowNumber, NoticeRowRules.SkuUnknown(r.Sku), null));
            var lines = g.Rows.Where(r => bySku.ContainsKey(r.Sku.ToUpperInvariant()))
                .GroupBy(r => (r.Sku.ToUpperInvariant(), r.Lot))
                .Select(x => (bySku[x.Key.Item1], x.Sum(r => r.Quantity ?? 0), x.Key.Lot)).ToList();
            if (lines.Count == 0) continue;
            if (missing.Count > 0) continue;   // un aviso a medias no se crea: todo el grupo queda en la bandeja para corregirlo
            try { created.Add(await CreateAsync(request.Client.PublicId, request.Reader?.WarehousePublicId, g, lines, ct)); }
            catch (TeikemException ex)
            {
                db.ChangeTracker.Clear();
                rejected.Add(new RejectedOrder(g.Reference, g.Rows[0].RowNumber, $"{Label} {(g.Reference.Length > 0 ? g.Reference : "(sin referencia)")}: {ex.Message}", null));
            }
        }
        log.Result = request.CheckDuplicate ? IntegrationFileResults.Processed : IntegrationFileResults.Reprocessed;
        log.OrdersCreated = created.Count;
        log.Rejected = rejected.Count;
        log.Message = $"{created.Count} {Label.ToLowerInvariant()}(s) creado(s), {rejected.Count} a la bandeja.";
        return new FileOutcome(log, created, rejected);
    }
}

/// <summary>Destino ARRIVAL_NOTICE: crea un aviso de llegada (ASN) del cliente por cada referencia del documento. El almacén es el del lector o, si no trae, el único activo de la compañía.</summary>
public sealed class ArrivalNoticeDocumentTarget(TeikemDbContext db, ITenantContext tenant, AsnService asns) : NoticeDocumentTarget(db, tenant)
{
    public override string Kind => DocumentTargets.ArrivalNotice;
    protected override bool WithDestination => false;
    protected override string Label => "Aviso de llegada";

    protected override async Task<Guid> CreateAsync(Guid clientPublicId, Guid? warehousePublicId, NoticeRowRules.Group group, IReadOnlyList<(Guid ProductPublicId, decimal Quantity, string? Lot)> lines, CancellationToken ct)
    {
        var dto = await asns.CreateAsync(new AsnCreateRequest(warehousePublicId, clientPublicId, string.IsNullOrWhiteSpace(group.Reference) ? null : group.Reference, group.Date,
            lines.Select(l => new AsnLineRequest(l.ProductPublicId, l.Quantity, l.Lot)).ToList()), ct);
        _ = dto;
        return Guid.Empty;   // el ASN se identifica por su número entero (no tiene PublicId); la corrida solo cuenta cuántos se crearon
    }
}

/// <summary>Destino EXIT_NOTICE: crea un aviso de salida del cliente por cada (referencia, destino, fecha) del documento; reserva lo que haya y deja el resto pendiente.</summary>
public sealed class ExitNoticeDocumentTarget(TeikemDbContext db, ITenantContext tenant, ExitNoticeService notices) : NoticeDocumentTarget(db, tenant)
{
    public override string Kind => DocumentTargets.ExitNotice;
    protected override bool WithDestination => true;
    protected override string Label => "Aviso de salida";

    protected override async Task<Guid> CreateAsync(Guid clientPublicId, Guid? warehousePublicId, NoticeRowRules.Group group, IReadOnlyList<(Guid ProductPublicId, decimal Quantity, string? Lot)> lines, CancellationToken ct)
    {
        var dto = await notices.CreateAsync(new ExitNoticeRequest(warehousePublicId, clientPublicId, string.IsNullOrWhiteSpace(group.Reference) ? null : group.Reference, group.Destination, group.Date, null,
            lines.Select(l => new ExitNoticeLineRequest(l.ProductPublicId, l.Quantity)).ToList()), ct);
        return dto.PublicId;
    }
}
