using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 3 (P9): importador de órdenes por plantilla de posición de columna, en DOS PASOS.
/// - Validar (ValidateAsync): parsea el CSV (CsvParser: comillas, delimitador de la plantilla, saltos dentro de comillas,
///   cabecera opcional; 5.000 filas / 2 MB → 400), mapea por posición + defaults (ImportMapping) y evalúa fila por fila
///   las MISMAS reglas que OrderService SIN guardar órdenes: tipo de servicio/paquete por código o default del tenant,
///   OrderRules.ValidatePackages/ValidateCod, numeración tecleada según NumberingRules, consignatario por consigneeCode
///   (Location.Code del cliente o compartida), por coincidencia normalizada nombre + línea 1 (ConsigneeMatch) o 'se creará'
///   con la dirección de la fila, número de orden ya usado (por cliente) y factura repetida R36 (bloqueada → error;
///   confirmable → aviso). Guarda un ImportBatch VALIDATED con RowsJson y devuelve la vista previa.
/// - Confirmar (ConfirmAsync): por cada fila válida elegida llama OrderService.CreateAsync (numeración, consignatario
///   existente/coincidente/al vuelo, R36 con confirmDuplicateInvoice, cliente dado de baja 409) en su propia transacción
///   (una fila fallida no tumba las demás); confirmNow confirma cada orden (cotiza, congela, crédito) y overrideCredit
///   (orders.credit_override) autoriza el exceso. Registra orderPublicId o el error por fila, pasa el lote a CONFIRMED
///   (StatusService) y devuelve el resultado. Un lote ya CONFIRMED responde 409; DiscardAsync → DISCARDED solo desde VALIDATED.
/// Todo bajo OrderScope (el portal fija el cliente; lotes de otro cliente responden 404, sin oráculo).
/// </summary>
public sealed class OrderImportService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    PermissionService permissions,
    ImportTemplateService templates,
    OrderService orders,
    OrderStatusService orderStatuses,
    INumberSequenceService sequences,
    ContactPointService contactPoints)
{
    public const string BatchNotFoundLabel = "Lote de importación";
    public const string ContentRequiredMessage = "Indique el contenido del archivo CSV.";
    public const string NoRowsMessage = "El archivo no tiene filas de datos.";
    public const string AlreadyConfirmedMessage = "El lote ya fue confirmado; valide un archivo nuevo para importar más órdenes.";
    public const string DiscardedMessage = "El lote fue descartado; valide el archivo de nuevo.";
    public const string DiscardOnlyValidatedMessage = "Solo se descarta un lote pendiente de confirmar.";
    public const string OverrideNeedsConfirmNowMessage = "overrideCredit requiere confirmNow=true.";
    public const string ConsigneeRequiredMessage = ImportMapping.ConsigneeRequiredMessage;
    public const string ConfirmInProgressMessage = "El lote ya se está confirmando o fue confirmado; consulte su resultado.";
    public const string ContactPhoneLabel = "Contacto de entrega";
    public const string UnexpectedRowErrorMessage = "Error inesperado al crear la orden; revise la fila e intente de nuevo.";

    /// <summary>Tipo de referencia (OrderRefType) con que se guarda la columna 'reference' del archivo.</summary>
    public const string ReferenceType = "OTHER";
    public const string ReferenceSource = "IMPORT";

    private static readonly JsonSerializerOptions RowsJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ---------------------------------------------------------------- contexto de validación (cargado una vez por archivo)

    private sealed record LocationRow(int LocationId, Guid PublicId, int? ClientId, string? Code, string Name, string Line1, int LocationTypeLookupId, bool IsActive, bool AllowDupInvoice);

    private sealed class RowContext
    {
        public required Client Client { get; init; }
        public required Tenant TenantRow { get; init; }
        public required NumberingSettings Settings { get; init; }
        public required IReadOnlySet<string> ServiceTypes { get; init; }
        public required IReadOnlySet<string> PackageTypes { get; init; }
        public required IReadOnlySet<string> Countries { get; init; }
        public required IReadOnlyList<LocationRow> Locations { get; init; }
        public required IReadOnlyList<(int LocationId, string Name, string Line1)> MatchCandidates { get; init; }
    }

    // ================================================================ VALIDAR

    public async Task<ImportPreviewDto> ValidateAsync(ImportValidateRequest req, OrderScope scope, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var userId = ((TenantContext)tenant).RequireUserId();

        if (string.IsNullOrWhiteSpace(req.Content)) throw new ValidationException("content", ContentRequiredMessage);
        if (CsvParser.ExceedsSize(req.Content)) throw new ValidationException("content", CsvParser.LimitMessage);

        var client = await ResolveClientForScopeAsync(scope, req.ClientPublicId, ct);
        await EnsureClientCanTakeOrdersAsync(client, ct);
        var template = await templates.ResolveForImportAsync(req.TemplatePublicId, client.ClientId, ImportKinds.Order, ct);
        var columns = ImportTemplateService.ParseColumns(template.ColumnsJson);
        var defaults = ImportTemplateService.ParseDefaults(template.DefaultsJson);

        IReadOnlyList<CsvRow> csvRows;
        try { csvRows = CsvParser.Parse(req.Content, ImportTemplateService.DelimiterOf(template), template.HasHeader); }
        catch (CsvLimitException) { throw new ValidationException("content", CsvParser.LimitMessage); }
        if (csvRows.Count == 0) throw new ValidationException("content", NoRowsMessage);

        var ctx = await LoadContextAsync(client, tenantId, ct);
        var records = csvRows.Select(r => ValidateRow(r, columns, defaults, ctx)).ToList();
        CheckDuplicatesWithinFile(records);
        await CheckExistingOrderNumbersAsync(records, client.ClientId, ct);
        await CheckDuplicateInvoicesAsync(records, ctx, scope, ct);

        var validRows = records.Count(r => r.IsValid);
        var fileName = string.IsNullOrWhiteSpace(req.FileName) ? null : req.FileName.Trim();
        if (fileName is { Length: > 260 }) fileName = fileName[..260];

        var batchPublicId = await db.RunInTransactionAsync(async ct2 =>
        {
            var initial = await statuses.GetInitialAsync(ImportBatchStatuses.Domain, ct2);
            var batch = new ImportBatch
            {
                TenantId = tenantId,
                Kind = ImportKinds.Order,
                ImportTemplateId = template.ImportTemplateId,
                ClientId = client.ClientId,
                FileName = fileName,
                RowCount = records.Count,
                ValidRows = validRows,
                StatusCodeId = initial.StatusCodeId,
                RowsJson = SerializeRows(records),
                CreatedBy = userId,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.ImportBatches.Add(batch);
            await db.SaveChangesAsync(ct2);
            var born = await statuses.TransitionAsync(ImportBatchStatuses.Domain, ImportEntityTypes.ImportBatch, batch.ImportBatchId, null, initial.InternalCode, null, ct2);
            batch.StatusCodeId = born.StatusCodeId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            return batch.PublicId;
        }, ct);

        return await GetAsync(batchPublicId, scope, ct);
    }

    // ================================================================ CONSULTAR

    public async Task<ImportPreviewDto> GetAsync(Guid batchPublicId, OrderScope scope, CancellationToken ct)
    {
        var batch = await LoadBatchAsync(batchPublicId, scope, ct);
        return await ToPreviewAsync(batch, ct);
    }

    // ================================================================ CONFIRMAR

    public async Task<ImportResultDto> ConfirmAsync(Guid batchPublicId, ImportConfirmRequest? req, OrderScope scope, CancellationToken ct)
    {
        req ??= new ImportConfirmRequest();
        var userId = ((TenantContext)tenant).RequireUserId();
        var batch = await LoadBatchAsync(batchPublicId, scope, ct);
        await EnsureValidatedAsync(batch, ct);
        if (req.OverrideCredit && !req.ConfirmNow) throw new ValidationException("overrideCredit", OverrideNeedsConfirmNowMessage);
        if (req.OverrideCredit) await permissions.EnsureAsync(OrderStatusService.CreditOverridePermission, ct); // 403 + PERMISSION_DENIED

        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == batch.ClientId, ct) ?? throw new NotFoundException("Cliente");
        await EnsureClientCanTakeOrdersAsync(client, ct);

        var records = DeserializeRows(batch.RowsJson);
        var selected = SelectRows(records, req.Rows);

        // Reserva atómica del lote ANTES de crear filas: un UPDATE condicionado a VALIDATED y ConfirmedAtUtc NULL (bajo el filtro
        // de tenant). Dos confirmaciones simultáneas (o confirmar y descartar a la vez) no pueden pasar ambas: la segunda afecta
        // 0 filas y responde 409 sin crear órdenes. ConfirmedAtUtc/ConfirmedBy se reescriben al cerrar el lote.
        var validatedId = await db.StatusIdAsync(ImportBatchStatuses.Domain, ImportBatchStatuses.Validated, ct);
        var reservedAt = DateTime.UtcNow;
        var reserved = await db.ImportBatches
            .Where(b => b.ImportBatchId == batch.ImportBatchId && b.StatusCodeId == validatedId && b.ConfirmedAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.ConfirmedAtUtc, reservedAt).SetProperty(b => b.ConfirmedBy, userId), ct);
        if (reserved == 0)
        {
            var current = await LoadBatchAsync(batchPublicId, scope, ct);
            await EnsureValidatedAsync(current, ct); // CONFIRMED / DISCARDED → su 409 específico
            throw new ConflictException(ConfirmInProgressMessage);
        }

        // Filas del contador en autocommit (idempotente) para que dentro de cada transacción solo corra el UPDATE.
        await sequences.EnsureAsync(NumberKinds.Order, NumberingRules.ScopeClientId(NumberKinds.Order, client.ClientId), ct);
        await sequences.EnsureAsync(NumberKinds.Invoice, NumberingRules.ScopeClientId(NumberKinds.Invoice, client.ClientId), ct);
        await sequences.EnsureAsync(NumberKinds.PackBatch, NumberingRules.ScopeClientId(NumberKinds.PackBatch, client.ClientId), ct);
        await sequences.EnsureAsync(NumberKinds.Package, NumberingRules.ScopeClientId(NumberKinds.Package, client.ClientId), ct);

        var options = new OrderCreationOptions(SourceEntityType: ImportEntityTypes.ImportBatch, SourceEntityId: batch.ImportBatchId);
        int created = 0, failed = 0, skipped = 0;
        foreach (var record in records)
        {
            if (!selected.Contains(record.Row))
            {
                record.Skipped = true;
                skipped++;
                continue;
            }
            record.Skipped = null;
            record.Error = null;
            var request = ToOrderRequest(record, client, req.ConfirmDuplicateInvoice);
            try
            {
                // Una transacción por fila: creación (y confirmación opcional) atómicas; un fallo revierte solo esta fila.
                var dto = await db.RunInTransactionAsync(async ct2 =>
                {
                    var createdDto = await orders.CreateAsync(request, scope, options, ct2);
                    // Teléfono de contacto de la fila → ContactPoint de la orden (capa C: nunca como texto suelto en notas).
                    if (record.Values.TryGetValue(ImportFields.ContactPhone, out var phone) && !string.IsNullOrWhiteSpace(phone))
                        await contactPoints.AddAsync(EntityTypes.TransportOrder, createdDto.Id,
                            new ContactPointUpsertRequest("PHONE", phone, null, ContactPhoneLabel, IsPrimary: true), ct2,
                            enforceOwnerWrite: false); // la importación ya exige orders.create sobre la orden que acaba de crear
                    if (req.ConfirmNow)
                        createdDto = await orderStatuses.ConfirmAsync(createdDto.PublicId, null, scope, req.OverrideCredit, ct2);
                    return createdDto;
                }, ct);
                record.OrderPublicId = dto.PublicId;
                record.OrderNumber = dto.OrderNumber;
                record.OrderStatus = dto.Status;
                created++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear(); // lo que quedó a medias no debe contaminar la fila siguiente
                db.PendingAudits.Clear(); // ni su bitácora pendiente escribirse con la fila siguiente
                record.Error = ex is TeikemException te ? te.Message : UnexpectedRowErrorMessage;
                failed++;
            }
        }

        await db.RunInTransactionAsync(async ct2 =>
        {
            var tracked = await db.ImportBatches.FirstOrDefaultAsync(b => b.ImportBatchId == batch.ImportBatchId, ct2)
                          ?? throw new NotFoundException(BatchNotFoundLabel);
            tracked.RowsJson = SerializeRows(records);
            tracked.ConfirmedAtUtc = DateTime.UtcNow;
            tracked.ConfirmedBy = userId;
            var to = await statuses.TransitionAsync(ImportBatchStatuses.Domain, ImportEntityTypes.ImportBatch, tracked.ImportBatchId,
                tracked.StatusCodeId, ImportBatchStatuses.Confirmed, $"Órdenes creadas: {created}; fallidas: {failed}; omitidas: {skipped}.", ct2);
            tracked.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        return new ImportResultDto(batch.PublicId, ImportBatchStatuses.Confirmed, created, failed, skipped, records.Select(r => r.ToDto()).ToList());
    }

    // ================================================================ DESCARTAR

    public async Task<ImportPreviewDto> DiscardAsync(Guid batchPublicId, OrderScope scope, CancellationToken ct)
    {
        var batch = await LoadBatchAsync(batchPublicId, scope, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var tracked = await db.ImportBatches.FirstOrDefaultAsync(b => b.ImportBatchId == batch.ImportBatchId, ct2)
                          ?? throw new NotFoundException(BatchNotFoundLabel);
            if (!await db.IsInitialAsync(tracked.StatusCodeId, ct2)) throw new StatusRuleException(DiscardOnlyValidatedMessage);
            if (tracked.ConfirmedAtUtc is not null) throw new ConflictException(ConfirmInProgressMessage);
            var to = await statuses.TransitionAsync(ImportBatchStatuses.Domain, ImportEntityTypes.ImportBatch, tracked.ImportBatchId,
                tracked.StatusCodeId, ImportBatchStatuses.Discarded, null, ct2);
            // Mismo candado que la reserva de ConfirmAsync: solo descarta si nadie reservó el lote entretanto (bloqueo de fila
            // hasta el commit; una confirmación concurrente verá DISCARDED y responderá 409).
            var validatedId = tracked.StatusCodeId;
            var locked = await db.ImportBatches
                .Where(b => b.ImportBatchId == tracked.ImportBatchId && b.StatusCodeId == validatedId && b.ConfirmedAtUtc == null)
                .ExecuteUpdateAsync(u => u.SetProperty(b => b.StatusCodeId, to.StatusCodeId), ct2);
            if (locked == 0) throw new ConflictException(ConfirmInProgressMessage);
            tracked.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(batchPublicId, scope, ct);
    }

    // ================================================================ validación fila a fila (sin guardar)

    private static ImportRowRecord ValidateRow(CsvRow row, IReadOnlyList<ImportColumn> columns, IReadOnlyDictionary<string, string> defaults, RowContext ctx)
    {
        var values = ImportMapping.Map(row.Cells, columns, defaults);
        var rec = new ImportRowRecord { Row = row.RowNumber, Line = row.Line, Values = new Dictionary<string, string>(values, StringComparer.Ordinal) };
        string? Get(string field) => values.TryGetValue(field, out var v) ? v : null;
        void Error(string field, string message) { if (!rec.Errors.ContainsKey(field)) rec.Errors[field] = message; }

        // Tipo de servicio y de paquete: código del catálogo o default del tenant
        if (Get(ImportFields.ServiceType) is string st)
        {
            var code = st.ToUpperInvariant();
            rec.Values[ImportFields.ServiceType] = code;
            if (!ctx.ServiceTypes.Contains(code)) Error(ImportFields.ServiceType, $"Tipo de servicio desconocido: {code}.");
        }
        else if (ctx.TenantRow.DefaultServiceTypeLookupId is null) Error(ImportFields.ServiceType, "El tipo de servicio es obligatorio.");

        if (Get(ImportFields.PackageType) is string pt)
        {
            var code = pt.ToUpperInvariant();
            rec.Values[ImportFields.PackageType] = code;
            if (!ctx.PackageTypes.Contains(code)) Error(ImportFields.PackageType, $"Tipo de paquete desconocido: {code}.");
        }
        else if (ctx.TenantRow.DefaultPackageTypeLookupId is null) Error(ImportFields.PackageType, "El tipo de paquete es obligatorio.");

        // Piezas, peso, volumen, COD (mismas reglas que la captura)
        var pieces = 1;
        if (Get(ImportFields.Pieces) is string piecesText)
        {
            if (ImportMapping.TryParseInt(piecesText, out var p)) pieces = p;
            else Error(ImportFields.Pieces, "La cantidad de piezas debe ser un número entero.");
        }
        var weight = ParseDecimal(rec, ImportFields.WeightKg, "El peso debe ser un número.");
        var volume = ParseDecimal(rec, ImportFields.VolumeM3, "El volumen debe ser un número.");
        var cod = ParseDecimal(rec, ImportFields.CodAmount, "El monto COD debe ser un número.");
        var line = new PackageLineInput(Get(ImportFields.Description), pieces, weight, volume);
        foreach (var (key, message) in OrderRules.ValidatePackages(new[] { line }, isSpecialDelivery: false, cod))
            Error(key.StartsWith("packages[0].", StringComparison.Ordinal) ? key["packages[0].".Length..] : ImportFields.Pieces, message);
        if (OrderRules.ValidateCod(cod) is string codError) Error(ImportFields.CodAmount, codError);

        // Fecha solicitada, referencia, país
        if (Get(ImportFields.RequestedDate) is string dateText && !ImportMapping.TryParseDate(dateText, out _))
            Error(ImportFields.RequestedDate, "La fecha solicitada no es válida; use yyyy-MM-dd.");
        if (Get(ImportFields.Reference) is { Length: > OrderRules.ReferenceValueMaxLength })
            Error(ImportFields.Reference, $"Máximo {OrderRules.ReferenceValueMaxLength} caracteres.");
        if (Get(ImportFields.Country) is string country)
        {
            var code = country.ToUpperInvariant();
            rec.Values[ImportFields.Country] = code;
            if (!CountryCode.IsValid(code)) Error(ImportFields.Country, CountryCode.InvalidMessage);
            else if (!ctx.Countries.Contains(code)) Error(ImportFields.Country, $"País desconocido: {code}.");
        }

        // Teléfono de contacto: misma validación que ContactPointService (se guarda como ContactPoint de la orden al confirmar)
        if (Get(ImportFields.ContactPhone) is string phoneText)
        {
            try { rec.Values[ImportFields.ContactPhone] = ContactPointService.ValidateValue("PHONE", phoneText); }
            catch (ValidationException ex) { Error(ImportFields.ContactPhone, ex.Message); }
        }

        // Numeración tecleada según las dos preguntas del cliente (mensajes exactos de NumberingRules)
        EnsureTyped(rec, NumberKinds.Order, ImportFields.OrderNumber, ctx.Settings);
        EnsureTyped(rec, NumberKinds.Invoice, ImportFields.ClientInvoiceNumber, ctx.Settings);

        // Consignatario: código del directorio → coincidencia nombre + línea 1 → se creará
        var consigneeCode = Get(ImportFields.ConsigneeCode);
        var name = Get(ImportFields.ConsigneeName);
        var line1 = Get(ImportFields.Line1);
        var city = Get(ImportFields.City);
        if (consigneeCode is not null)
        {
            var loc = ctx.Locations
                .Where(l => string.Equals(l.Code, consigneeCode, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(l => l.IsActive).ThenByDescending(l => l.ClientId.HasValue).ThenBy(l => l.LocationId)
                .FirstOrDefault();
            if (loc is null) Error(ImportFields.ConsigneeCode, $"No existe un consignatario con el código '{consigneeCode}' para este cliente.");
            else if (!loc.IsActive) Error(ImportFields.ConsigneeCode, $"El consignatario '{loc.Name}' está inactivo; reactívelo o elija otro.");
            else rec.Consignee = new ImportRowConsigneeRecord { Action = "EXISTING", Name = loc.Name, LocationPublicId = loc.PublicId };
        }
        else if (name is not null)
        {
            var matchId = line1 is null ? null : ConsigneeMatch.FindMatch(ctx.MatchCandidates, name, line1);
            if (matchId is int id)
            {
                var loc = ctx.Locations.First(l => l.LocationId == id);
                rec.Consignee = new ImportRowConsigneeRecord { Action = "EXISTING", Name = loc.Name, LocationPublicId = loc.PublicId };
            }
            else
            {
                if (name.Length > 200) Error(ImportFields.ConsigneeName, "Máximo 200 caracteres.");
                if (line1 is null) Error(ImportFields.Line1, "La dirección (línea 1) es obligatoria para crear el consignatario.");
                else if (line1.Length > 200) Error(ImportFields.Line1, "Máximo 200 caracteres.");
                if (city is null) Error(ImportFields.City, "La ciudad es obligatoria para crear el consignatario.");
                else if (city.Length > 100) Error(ImportFields.City, "Máximo 100 caracteres.");
                if (Get(ImportFields.Line2) is { Length: > 200 }) Error(ImportFields.Line2, "Máximo 200 caracteres.");
                if (Get(ImportFields.State) is { Length: > 100 }) Error(ImportFields.State, "Máximo 100 caracteres.");
                if (Get(ImportFields.PostalCode) is { Length: > 20 }) Error(ImportFields.PostalCode, "Máximo 20 caracteres.");
                rec.Consignee = new ImportRowConsigneeRecord { Action = "CREATE", Name = name };
            }
        }
        else if (ImportMapping.RequireConsignee(values) is (string consigneeField, string consigneeMessage))
        {
            Error(consigneeField, consigneeMessage);
        }

        return rec;
    }

    private static decimal? ParseDecimal(ImportRowRecord rec, string field, string message)
    {
        if (!rec.Values.TryGetValue(field, out var text)) return null;
        if (ImportMapping.TryParseDecimal(text, out var value)) return value;
        rec.Errors.TryAdd(field, message);
        return null;
    }

    private static void EnsureTyped(ImportRowRecord rec, string kind, string field, NumberingSettings settings)
    {
        if (!rec.Values.TryGetValue(field, out var typed)) return;
        try
        {
            var normalized = NumberingRules.EnsureTypedAllowed(kind, settings, typed);
            if (normalized is null) rec.Values.Remove(field); else rec.Values[field] = normalized;
        }
        catch (ArgumentException ex) { rec.Errors.TryAdd(field, ex.Message); }
    }

    /// <summary>Número de orden repetido dentro del archivo → error; factura repetida en el archivo para el mismo consignatario → aviso.</summary>
    private static void CheckDuplicatesWithinFile(List<ImportRowRecord> records)
    {
        var orderNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var invoices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
        {
            if (r.Values.TryGetValue(ImportFields.OrderNumber, out var number) && !r.Errors.ContainsKey(ImportFields.OrderNumber))
            {
                if (orderNumbers.TryGetValue(number, out var firstRow))
                    r.Errors[ImportFields.OrderNumber] = $"El número de orden {number} se repite en la fila {firstRow} del archivo.";
                else orderNumbers[number] = r.Row;
            }
            if (r.Values.TryGetValue(ImportFields.ClientInvoiceNumber, out var invoice) && r.Consignee?.LocationPublicId is Guid loc)
            {
                var key = invoice + "|" + loc;
                if (invoices.TryGetValue(key, out var firstRow))
                    r.Warnings.Add($"La factura {invoice} se repite en la fila {firstRow} del archivo para el mismo consignatario.");
                else invoices[key] = r.Row;
            }
        }
    }

    /// <summary>Número de orden tecleado que ya usa una orden activa del cliente (mismo 409 que daría la captura).</summary>
    private async Task CheckExistingOrderNumbersAsync(List<ImportRowRecord> records, int clientId, CancellationToken ct)
    {
        var typed = records.Where(r => !r.Errors.ContainsKey(ImportFields.OrderNumber) && r.Values.ContainsKey(ImportFields.OrderNumber))
            .Select(r => r.Values[ImportFields.OrderNumber]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (typed.Count == 0) return;
        var existing = await db.TransportOrders.AsNoTracking()
            .Where(o => o.IsActive && o.ClientId == clientId && typed.Contains(o.OrderNumber))
            .Select(o => o.OrderNumber).ToListAsync(ct);
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
            if (r.Values.TryGetValue(ImportFields.OrderNumber, out var n) && taken.Contains(n))
                r.Errors.TryAdd(ImportFields.OrderNumber, OrderService.OrderNumberTakenMessage);
    }

    /// <summary>R36 informativa: factura tecleada ya usada por una orden activa del mismo consignatario (bloqueada → error; confirmable → aviso).</summary>
    private async Task CheckDuplicateInvoicesAsync(List<ImportRowRecord> records, RowContext ctx, OrderScope scope, CancellationToken ct)
    {
        var candidates = records.Where(r => r.Values.ContainsKey(ImportFields.ClientInvoiceNumber) && !r.Errors.ContainsKey(ImportFields.ClientInvoiceNumber)
                                             && r.Consignee?.LocationPublicId is not null).ToList();
        if (candidates.Count == 0) return;
        var invoices = candidates.Select(r => r.Values[ImportFields.ClientInvoiceNumber]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existingOrders = await db.TransportOrders.AsNoTracking()
            .Where(o => o.IsActive && invoices.Contains(o.ClientInvoiceNumber))
            .OrderByDescending(o => o.TransportOrderId)
            .Select(o => new { o.TransportOrderId, o.OrderNumber, o.PublicId, o.ClientInvoiceNumber, o.ClientId })
            .ToListAsync(ct);
        if (existingOrders.Count == 0) return;
        var orderIds = existingOrders.Select(o => o.TransportOrderId).ToList();
        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
        var stops = await db.OrderStops.AsNoTracking()
            .Where(s => orderIds.Contains(s.TransportOrderId) && s.StopTypeLookupId == deliveryTypeId && s.LocationId != null)
            .Select(s => new { s.TransportOrderId, LocationId = s.LocationId!.Value })
            .ToListAsync(ct);
        var deliveryByOrder = stops.GroupBy(s => s.TransportOrderId).ToDictionary(g => g.Key, g => g.Select(s => s.LocationId).ToHashSet());

        foreach (var r in candidates)
        {
            var loc = ctx.Locations.FirstOrDefault(l => l.PublicId == r.Consignee!.LocationPublicId);
            if (loc is null) continue;
            var invoice = r.Values[ImportFields.ClientInvoiceNumber];
            var existing = existingOrders.FirstOrDefault(o => string.Equals(o.ClientInvoiceNumber, invoice, StringComparison.OrdinalIgnoreCase)
                                                              && deliveryByOrder.TryGetValue(o.TransportOrderId, out var locs) && locs.Contains(loc.LocationId));
            // Con el scope fijado (portal), una orden de otro cliente no se nombra (sin oráculo entre clientes).
            var foreign = existing is not null && OrderRules.IsForeignDuplicate(scope.ClientId, existing.ClientId);
            switch (OrderRules.DecideDuplicateInvoice(true, existing?.OrderNumber, loc.AllowDupInvoice, confirmed: false))
            {
                case DuplicateInvoiceDecision.Blocked:
                    r.Errors.TryAdd(ImportFields.ClientInvoiceNumber, foreign
                        ? OrderService.DuplicateInvoiceBlockedForeignMessage(loc.Name, invoice)
                        : $"El consignatario '{loc.Name}' no permite facturas repetidas: la orden {existing!.OrderNumber} ya usa el número {invoice}.");
                    break;
                case DuplicateInvoiceDecision.NeedsConfirmation:
                    r.Warnings.Add(foreign
                        ? DuplicateInvoiceForeignWarning(loc.Name, invoice)
                        : $"El consignatario '{loc.Name}' ya tiene la factura {invoice} en la orden {existing!.OrderNumber}; la fila se creará solo si confirma facturas repetidas (confirmDuplicateInvoice).");
                    break;
            }
        }
    }

    /// <summary>Aviso R36 de la validación con el scope fijado (portal) y la orden existente de otro cliente: no nombra esa orden.</summary>
    public static string DuplicateInvoiceForeignWarning(string consigneeName, string invoice)
        => $"El consignatario '{consigneeName}' ya tiene la factura {invoice}; la fila se creará solo si confirma facturas repetidas (confirmDuplicateInvoice).";

    // ================================================================ contexto y carga

    private async Task<RowContext> LoadContextAsync(Client client, int tenantId, CancellationToken ct)
    {
        var tenantRow = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct) ?? throw new NotFoundException("Tenant");
        var serviceTypes = await ActiveCodesAsync(LookupDomains.ServiceType, ct);
        var packageTypes = await ActiveCodesAsync(LookupDomains.PackageType, ct);
        var countries = await ActiveCodesAsync(LookupDomains.Country, ct);
        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Delivery, ct);
        var bothTypeId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Both, ct);

        var clientId = client.ClientId;
        var locations = await db.Locations.AsNoTracking()
            .Where(l => l.ClientId == clientId || l.ClientId == null)
            .OrderBy(l => l.LocationId)
            .Select(l => new LocationRow(l.LocationId, l.PublicId, l.ClientId, l.Code, l.Name, l.Line1, l.LocationTypeLookupId, l.IsActive, l.AllowDupInvoice))
            .ToListAsync(ct);
        // Coincidencia por nombre + línea 1 solo contra las DELIVERY/BOTH activas del propio cliente (nunca compartidas), como OrderService.
        var candidates = locations
            .Where(l => l.ClientId == clientId && l.IsActive && (l.LocationTypeLookupId == deliveryTypeId || l.LocationTypeLookupId == bothTypeId))
            .Select(l => (l.LocationId, l.Name, l.Line1)).ToList();

        return new RowContext
        {
            Client = client,
            TenantRow = tenantRow,
            Settings = NumberingSettings.FromClient(client),
            ServiceTypes = serviceTypes,
            PackageTypes = packageTypes,
            Countries = countries,
            Locations = locations,
            MatchCandidates = candidates,
        };
    }

    /// <summary>Códigos activos del dominio, sin los que el tenant deshabilitó con su override (L54/L1158), como la captura.</summary>
    private async Task<IReadOnlySet<string>> ActiveCodesAsync(string domain, CancellationToken ct)
    {
        var disabled = (await db.LookupCodeOverrides.AsNoTracking().Where(o => !o.IsEnabled).Select(o => o.LookupCodeId).ToListAsync(ct)).ToHashSet();
        return (await lookups.GetDomainAsync(domain, ct))
            .Where(l => l.IsActive && !disabled.Contains(l.LookupCodeId))
            .Select(l => l.InternalCode.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Client> ResolveClientForScopeAsync(OrderScope scope, Guid? clientPublicId, CancellationToken ct)
    {
        if (scope.ClientId is int scopedClientId)
            return await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == scopedClientId, ct) ?? throw new NotFoundException("Cliente");
        if (clientPublicId is null || clientPublicId == Guid.Empty) throw new ValidationException("clientPublicId", "El cliente es obligatorio.");
        return await db.ResolveClientAsync(clientPublicId.Value, ct);
    }

    /// <summary>Dado de baja → 409 (ajuste A); SUSPENDED → 422 (DECISIÓN 16): mismas guardas que OrderService.</summary>
    private async Task EnsureClientCanTakeOrdersAsync(Client client, CancellationToken ct)
    {
        ClientQueries.EnsureClientActive(client);
        var suspendedId = await db.StatusIdAsync(StatusDomains.ClientStatus, ClientStatuses.Suspended, ct);
        if (client.StatusCodeId == suspendedId) throw new StatusRuleException(OrderService.ClientSuspendedMessage);
    }

    private async Task<ImportBatch> LoadBatchAsync(Guid publicId, OrderScope scope, CancellationToken ct)
    {
        var q = db.ImportBatches.AsNoTracking().Where(b => b.PublicId == publicId && b.Kind == ImportKinds.Order);
        if (scope.ClientId is int clientId) q = q.Where(b => b.ClientId == clientId);
        return await q.FirstOrDefaultAsync(ct) ?? throw new NotFoundException(BatchNotFoundLabel);
    }

    private async Task EnsureValidatedAsync(ImportBatch batch, CancellationToken ct)
    {
        var code = await StatusCodeOfAsync(batch.StatusCodeId, ct);
        if (string.Equals(code, ImportBatchStatuses.Confirmed, StringComparison.OrdinalIgnoreCase)) throw new ConflictException(AlreadyConfirmedMessage);
        if (string.Equals(code, ImportBatchStatuses.Discarded, StringComparison.OrdinalIgnoreCase)) throw new ConflictException(DiscardedMessage);
        if (!string.Equals(code, ImportBatchStatuses.Validated, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException($"El lote está en estatus {code} y no admite confirmación.");
    }

    private async Task<string> StatusCodeOfAsync(int statusCodeId, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException("Estatus", statusCodeId);

    private static HashSet<int> SelectRows(List<ImportRowRecord> records, IList<int>? requested)
    {
        if (requested is null || requested.Count == 0)
            return records.Where(r => r.IsValid).Select(r => r.Row).ToHashSet();

        var byRow = records.ToDictionary(r => r.Row);
        var errors = new Dictionary<string, string[]>();
        foreach (var row in requested.Distinct())
        {
            if (!byRow.TryGetValue(row, out var rec)) errors[$"rows[{row}]"] = new[] { $"La fila {row} no existe en el lote." };
            else if (!rec.IsValid) errors[$"rows[{row}]"] = new[] { $"La fila {row} tiene errores y no se puede confirmar." };
        }
        if (errors.Count > 0) throw new ValidationException(errors);
        return requested.ToHashSet();
    }

    /// <summary>Mismo request que POST /orders: cada fila se crea con el comportamiento de la captura (numeración, consignatario, R36).</summary>
    private static OrderCreateRequest ToOrderRequest(ImportRowRecord r, Client client, bool confirmDuplicateInvoice)
    {
        string? Get(string field) => r.Values.TryGetValue(field, out var v) ? v : null;

        var pieces = ImportMapping.TryParseInt(Get(ImportFields.Pieces), out var p) ? p : 1;
        decimal? weight = ImportMapping.TryParseDecimal(Get(ImportFields.WeightKg), out var w) ? w : null;
        decimal? volume = ImportMapping.TryParseDecimal(Get(ImportFields.VolumeM3), out var v) ? v : null;
        decimal? cod = ImportMapping.TryParseDecimal(Get(ImportFields.CodAmount), out var c) ? c : null;
        DateTime? requestedDate = ImportMapping.TryParseDate(Get(ImportFields.RequestedDate), out var d) ? d : null;

        var notes = Get(ImportFields.Notes);

        OrderConsigneeCreateRequest? newConsignee = null;
        Guid? consigneePublicId = null;
        if (r.Consignee?.Action == "CREATE")
        {
            newConsignee = new OrderConsigneeCreateRequest(
                Name: r.Consignee.Name,
                Line1: Get(ImportFields.Line1) ?? string.Empty,
                Line2: Get(ImportFields.Line2),
                City: Get(ImportFields.City) ?? string.Empty,
                State: Get(ImportFields.State),
                PostalCode: Get(ImportFields.PostalCode),
                Country: Get(ImportFields.Country),
                Code: null);
        }
        else
        {
            consigneePublicId = r.Consignee?.LocationPublicId;
        }

        var references = Get(ImportFields.Reference) is string reference
            ? new List<OrderReferenceRequest> { new(RefType: ReferenceType, Value: reference, Source: ReferenceSource) }
            : null;

        return new OrderCreateRequest(
            ClientPublicId: client.PublicId,
            ConsigneeLocationPublicId: consigneePublicId,
            NewConsignee: newConsignee,
            PickupLocationPublicId: null,
            ServiceType: Get(ImportFields.ServiceType),
            Priority: null,
            Packages: new List<OrderPackageLineRequest>
            {
                new(PackageType: Get(ImportFields.PackageType), Description: Get(ImportFields.Description), Pieces: pieces, WeightKg: weight, VolumeM3: volume),
            },
            CodAmount: cod,
            OrderNumber: Get(ImportFields.OrderNumber),
            ClientInvoiceNumber: Get(ImportFields.ClientInvoiceNumber),
            RequestedDate: requestedDate,
            PromisedDate: null,
            Notes: notes,
            References: references,
            IsSpecialDelivery: false,
            SpecialServiceId: null,
            ConfirmDuplicateInvoice: confirmDuplicateInvoice,
            ConfirmNow: false);
    }

    private async Task<ImportPreviewDto> ToPreviewAsync(ImportBatch batch, CancellationToken ct)
    {
        var status = await StatusCodeOfAsync(batch.StatusCodeId, ct);
        var templateId = batch.ImportTemplateId;
        var template = await db.ImportTemplates.AsNoTracking().Where(t => t.ImportTemplateId == templateId)
            .Select(t => new { t.PublicId, t.Name }).FirstOrDefaultAsync(ct);
        var clientId = batch.ClientId;
        var client = await db.Clients.AsNoTracking().Where(c => c.ClientId == clientId)
            .Select(c => new { c.PublicId, c.Name }).FirstOrDefaultAsync(ct);
        var rows = DeserializeRows(batch.RowsJson);
        var confirmed = batch.ConfirmedAtUtc is not null;
        return new ImportPreviewDto(
            batch.PublicId, status,
            template?.PublicId ?? Guid.Empty, template?.Name ?? string.Empty,
            client?.PublicId ?? Guid.Empty, client?.Name ?? string.Empty,
            batch.FileName, batch.RowCount, batch.ValidRows,
            rows.Select(r => r.ToDto()).ToList(),
            batch.CreatedAtUtc, batch.ConfirmedAtUtc,
            confirmed ? rows.Count(r => r.OrderPublicId is not null) : null,
            confirmed ? rows.Count(r => r.Error is not null) : null);
    }

    private static string SerializeRows(IEnumerable<ImportRowRecord> rows) => JsonSerializer.Serialize(rows, RowsJsonOptions);

    private static List<ImportRowRecord> DeserializeRows(string? json)
        => string.IsNullOrWhiteSpace(json) ? new List<ImportRowRecord>() : JsonSerializer.Deserialize<List<ImportRowRecord>>(json, RowsJsonOptions) ?? new List<ImportRowRecord>();
}
