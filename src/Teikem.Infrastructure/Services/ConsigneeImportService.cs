using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 38 (F-B): importación CSV de consignatarios (dbo.Location tipo DELIVERY) por plantilla de posición de columna, en
/// DOS PASOS, sobre las mismas tablas que el importador de órdenes (ImportTemplate/ImportBatch con Kind CONSIGNEE).
/// - Validar (ValidateAsync): parsea el CSV (CsvParser: 5.000 filas / 2 MB → 400), mapea por posición + defaults
///   (ConsigneeImportMapping) y evalúa cada fila SIN crear nada: textos obligatorios y topes (los de LocationService), país,
///   minutos de servicio, ventana horaria, allowDupInvoice, teléfono (ContactPointService.ValidateValue), dueño de la fila
///   (clientCode → clientName → cliente del lote → compartida) y duplicados por dueño (código y nombre + línea 1, en el archivo
///   y contra el directorio). Guarda un ImportBatch VALIDATED con RowsJson y devuelve la vista previa.
/// - Confirmar (ConfirmAsync): reserva atómica del lote (UPDATE condicionado a VALIDATED, como órdenes) y, por cada fila
///   válida elegida, una transacción propia que serializa por tenant (bloqueo U de la fila Tenant, el mismo de 'Planificar el
///   día'), vuelve a revisar el cliente activo y los duplicados contra lo que se haya creado entretanto, crea la Location
///   (LocationService.CreateCoreAsync) y su teléfono principal (ContactPoint PHONE). Una fila que falla queda con su error y
///   no tumba las demás. El lote pasa a CONFIRMED vía StatusService con el resumen. DiscardAsync → DISCARDED desde VALIDATED.
/// Solo altas: un código existente es error (no se actualiza el directorio).
/// Lote 45 (F-B2): el archivo puede ser CSV o Excel .xlsx (ImportFileReader; hoja elegible), las columnas «No usar» se ignoran,
/// un campo puede unir varias columnas (ConsigneeImportMapping.Map), el cliente del lote es el cliente POR DEFECTO (la fila con su
/// propio cliente manda) y se importa la persona de contacto (Location.ContactName). InspectFile revisa el archivo sin guardar.
/// Lote 46 (F-B3): en Puerto Rico y Estados Unidos el pueblo, el estado y el ZIP se completan con el catálogo de códigos postales
/// (PostalInference: solo ZIP de ENTREGA; un ZIP de apartados o militar se sustituye por uno de entrega de la misma ciudad, con
/// aviso; ZIP o pueblo desconocidos rechazan la fila) y la plantilla puede tener variantes de mapeo con condición por fila
/// (ConsigneeImportVariants: la primera que se cumpla; ninguna → la fila se rechaza).
/// </summary>
public sealed class ConsigneeImportService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    ImportTemplateService templates,
    LocationService locations,
    ContactPointService contactPoints,
    PostalLocalityService postalLocalities)
{
    public const string BatchNotFoundLabel = OrderImportService.BatchNotFoundLabel;
    public const string ContentRequiredMessage = OrderImportService.ContentRequiredMessage;
    public const string NoRowsMessage = OrderImportService.NoRowsMessage;
    public const string AlreadyConfirmedMessage = "El lote ya fue confirmado; valide un archivo nuevo para importar más consignatarios.";
    public const string DiscardedMessage = OrderImportService.DiscardedMessage;
    public const string DiscardOnlyValidatedMessage = OrderImportService.DiscardOnlyValidatedMessage;
    public const string ConfirmInProgressMessage = OrderImportService.ConfirmInProgressMessage;
    public const string NoValidRowsMessage = "El lote no tiene filas válidas para crear.";
    public const string ContactPhoneLabel = "Teléfono";
    public const string UnexpectedRowErrorMessage = "Error inesperado al crear el consignatario; revise la fila e intente de nuevo.";

    public static string ClientCodeNotFoundMessage(string code) => $"No existe un cliente con el código '{code}'.";
    public static string ClientNameNotFoundMessage(string name) => $"No existe un cliente con el nombre '{name}'.";
    public static string ClientNameAmbiguousMessage(string name) => $"Hay más de un cliente con el nombre '{name}'; use clientCode.";

    private static readonly JsonSerializerOptions RowsJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record ClientRow(int ClientId, Guid PublicId, string Code, string Name, bool IsActive);

    private sealed class RowContext
    {
        public ClientRow? BatchClient { get; init; }
        public required IReadOnlyList<ClientRow> Clients { get; init; }
        public required IReadOnlySet<string> Countries { get; init; }
        /// <summary>Lote 46 (F-B3): catálogo de códigos postales en memoria para deducir pueblo, estado y ZIP.</summary>
        public required PostalIndex Postal { get; init; }
    }

    // ================================================================ CATÁLOGO

    /// <summary>Campos que una plantilla de consignatarios puede mapear (para el editor de plantillas).</summary>
    public static IReadOnlyList<ImportFieldDto> GetFields()
        => ConsigneeImportFields.All
            .Select(f => new ImportFieldDto(f, ConsigneeImportFields.Required.Contains(f), !ConsigneeImportFields.NoDefault.Contains(f)))
            .ToList();

    // ================================================================ VALIDAR

    /// <summary>Validar con el CSV en el cuerpo JSON (content).</summary>
    public Task<ConsigneeImportPreviewDto> ValidateAsync(ConsigneeImportValidateRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Content)) throw new ValidationException("content", ContentRequiredMessage);
        if (CsvParser.ExceedsSize(req.Content)) throw new ValidationException("content", CsvParser.LimitMessage);
        var content = req.Content;
        return ValidateCoreAsync(req.TemplatePublicId, req.ClientPublicId, req.FileName, "content",
            template => CsvParser.Parse(content, ImportTemplateService.DelimiterOf(template), template.HasHeader), ct);
    }

    /// <summary>
    /// Lote 45 (F-B2): validar el archivo subido, CSV (UTF-8) o Excel .xlsx (la hoja pedida o la primera), con el mismo flujo
    /// (mapeo, vista previa y errores por fila). 400 'file' si el formato no se admite o el libro no se puede leer.
    /// </summary>
    public Task<ConsigneeImportPreviewDto> ValidateFileAsync(Guid templatePublicId, Guid? clientPublicId, string? fileName, byte[] bytes, string? sheet, CancellationToken ct)
    {
        if (bytes is null || bytes.Length == 0) throw new ValidationException("file", ContentRequiredMessage);
        if (bytes.Length > CsvParser.MaxBytes) throw new ValidationException("file", CsvParser.LimitMessage);
        return ValidateCoreAsync(templatePublicId, clientPublicId, fileName, "file",
            template => ImportFileReader.Read(bytes, fileName, ImportTemplateService.DelimiterOf(template), template.HasHeader, sheet).Rows, ct);
    }

    /// <summary>
    /// Lote 45 (F-B2): revisa el archivo SIN guardar nada (para el editor de plantillas): formato, hojas, hoja leída, cuántas
    /// filas de datos trae y, por columna, el encabezado y hasta 3 valores de muestra. Mismos límites que validar.
    /// </summary>
    public static ImportFileInspectionDto InspectFile(byte[] bytes, string? fileName, string? delimiter, bool hasHeader, string? sheet)
    {
        if (bytes is null || bytes.Length == 0) throw new ValidationException("file", ContentRequiredMessage);
        ImportFileTable table;
        try { table = ImportFileReader.Read(bytes, fileName, ImportTemplateService.NormalizeDelimiter(delimiter), hasHeader, sheet); }
        catch (CsvLimitException) { throw new ValidationException("file", CsvParser.LimitMessage); }

        var width = Math.Min(ImportMapping.MaxColumns, Math.Max(table.Header?.Count ?? 0, table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Cells.Count)));
        var columns = Enumerable.Range(0, width).Select(i => new ImportFileColumnDto(
            i + 1,
            table.Header is { } h && i < h.Count && !string.IsNullOrWhiteSpace(h[i]) ? h[i].Trim() : null,
            table.Rows.Select(r => i < r.Cells.Count ? r.Cells[i].Trim() : string.Empty).Where(v => v.Length > 0).Take(3).ToList())).ToList();
        var sampleRows = table.Rows.Take(3).Select(r => (IReadOnlyList<string>)r.Cells.Take(width).Select(c => c.Trim()).ToList()).ToList();
        return new ImportFileInspectionDto(table.Format, table.Sheets, table.Sheet, table.Rows.Count, columns, sampleRows);
    }

    private async Task<ConsigneeImportPreviewDto> ValidateCoreAsync(Guid templatePublicId, Guid? clientPublicId, string? rawFileName, string contentField,
        Func<ImportTemplate, IReadOnlyList<CsvRow>> parse, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var userId = ((TenantContext)tenant).RequireUserId();

        Client? batchClient = null;
        if (clientPublicId is Guid cpid && cpid != Guid.Empty)
            batchClient = ClientQueries.EnsureClientActive(await db.ResolveClientAsync(cpid, ct)); // 404 'Cliente' / 409 dado de baja
        var template = await templates.ResolveForImportAsync(templatePublicId, batchClient?.ClientId, ImportKinds.Consignee, ct);
        var columns = ImportTemplateService.ParseColumns(template.ColumnsJson);
        var defaults = ImportTemplateService.ParseDefaults(template.DefaultsJson);
        var variants = ImportTemplateService.ParseVariants(template.VariantsJson);

        IReadOnlyList<CsvRow> csvRows;
        try { csvRows = parse(template); }
        catch (CsvLimitException) { throw new ValidationException(contentField, CsvParser.LimitMessage); }
        if (csvRows.Count == 0) throw new ValidationException(contentField, NoRowsMessage);

        var clients = await db.Clients.AsNoTracking()
            .OrderBy(c => c.ClientId)
            .Select(c => new ClientRow(c.ClientId, c.PublicId, c.Code, c.Name, c.IsActive))
            .ToListAsync(ct);
        var ctx = new RowContext
        {
            BatchClient = batchClient is null ? null : clients.FirstOrDefault(c => c.ClientId == batchClient.ClientId),
            Clients = clients,
            Countries = await ActiveCountriesAsync(ct),
            Postal = await postalLocalities.IndexAsync(ct),
        };

        var records = csvRows.Select(r => variants.Count == 0 ? ValidateRow(r, columns, defaults, ctx) : ValidateVariantRow(r, variants, defaults, ctx)).ToList();
        ApplyFileDuplicates(records);
        var directory = ConsigneeImportMapping.BuildDirectory(await LoadDirectoryAsync(records, afterLocationId: 0, ct));
        foreach (var r in records.Where(r => r.OwnerResolved))
            Apply(r, directory.Evaluate(KeyOf(r, onlyValidFields: true)));

        var validRows = records.Count(r => r.IsValid);
        var fileName = string.IsNullOrWhiteSpace(rawFileName) ? null : rawFileName.Trim();
        if (fileName is { Length: > 260 }) fileName = fileName[..260];

        var batchPublicId = await db.RunInTransactionAsync(async ct2 =>
        {
            var initial = await statuses.GetInitialAsync(ImportBatchStatuses.Domain, ct2);
            var batch = new ImportBatch
            {
                TenantId = tenantId,
                Kind = ImportKinds.Consignee,
                ImportTemplateId = template.ImportTemplateId,
                ClientId = batchClient?.ClientId,
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

        return await GetAsync(batchPublicId, ct);
    }

    // ================================================================ CONSULTAR

    public async Task<ConsigneeImportPreviewDto> GetAsync(Guid batchPublicId, CancellationToken ct)
    {
        var batch = await LoadBatchAsync(batchPublicId, ct);
        return await ToPreviewAsync(batch, ct);
    }

    // ================================================================ CONFIRMAR

    public async Task<ConsigneeImportResultDto> ConfirmAsync(Guid batchPublicId, ConsigneeImportConfirmRequest? req, CancellationToken ct)
    {
        req ??= new ConsigneeImportConfirmRequest();
        var userId = ((TenantContext)tenant).RequireUserId();
        var batch = await LoadBatchAsync(batchPublicId, ct);
        await EnsureValidatedAsync(batch, ct);
        if (batch.ClientId is int batchClientId)
        {
            var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == batchClientId, ct) ?? throw new NotFoundException("Cliente");
            ClientQueries.EnsureClientActive(client); // 409: el cliente del lote se dio de baja después de validar
        }

        var records = DeserializeRows(batch.RowsJson);
        var selected = SelectRows(records, req.Rows);

        // Reserva atómica del lote ANTES de crear filas (mismo candado que el importador de órdenes): dos confirmaciones
        // simultáneas (o confirmar y descartar a la vez) no pueden pasar ambas; la segunda afecta 0 filas y responde 409.
        var validatedId = await db.StatusIdAsync(ImportBatchStatuses.Domain, ImportBatchStatuses.Validated, ct);
        var reservedAt = DateTime.UtcNow;
        var reserved = await db.ImportBatches
            .Where(b => b.ImportBatchId == batch.ImportBatchId && b.StatusCodeId == validatedId && b.ConfirmedAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.ConfirmedAtUtc, reservedAt).SetProperty(b => b.ConfirmedBy, userId), ct);
        if (reserved == 0)
        {
            var current = await LoadBatchAsync(batchPublicId, ct);
            await EnsureValidatedAsync(current, ct); // CONFIRMED / DISCARDED → su 409 específico
            throw new ConflictException(ConfirmInProgressMessage);
        }

        // Foto del directorio de los dueños elegidos; cada fila la completa con lo creado después (LocationId mayor) bajo el bloqueo.
        var chosen = records.Where(r => selected.Contains(r.Row)).ToList();
        var existing = await LoadDirectoryAsync(chosen, afterLocationId: 0, ct);
        var directory = ConsigneeImportMapping.BuildDirectory(existing);
        var lastSeen = existing.Count == 0 ? 0 : existing.Max(e => e.LocationId);

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
            var request = ToLocationRequest(record);
            var key = KeyOf(record, onlyValidFields: false);
            try
            {
                // Una transacción por fila: revisión + alta + teléfono atómicos; un fallo revierte solo esta fila.
                var locationPublicId = await db.RunInTransactionAsync(async ct2 =>
                {
                    // Serializa la creación por tenant: Location.Code no tiene índice único, así que la revisión de duplicados
                    // y el alta deben ocurrir sin que otra importación concurrente cree el mismo código entretanto.
                    await db.LockTenantPlanningAsync(ct2);
                    var fresh = await LoadDirectoryAsync(chosen, lastSeen, ct2);
                    directory.AddRange(fresh);
                    if (fresh.Count > 0) lastSeen = Math.Max(lastSeen, fresh.Max(e => e.LocationId));

                    if (record.OwnerClientId is int ownerId)
                    {
                        var active = await db.Clients.AsNoTracking().Where(c => c.ClientId == ownerId).Select(c => (bool?)c.IsActive).FirstOrDefaultAsync(ct2);
                        if (active is null) throw new NotFoundException("Cliente");
                        if (active == false) throw new ConflictException(ClientQueries.ClientInactiveMessage);
                    }
                    var blocking = directory.Evaluate(key).FirstOrDefault(f => !f.IsWarning);
                    if (blocking is not null) throw new ConflictException(blocking.Message);

                    var (locationId, publicId) = await locations.CreateCoreAsync(request, record.OwnerClientId, ct2);
                    if (record.Values.TryGetValue(ConsigneeImportFields.ContactPhone, out var phone) && !string.IsNullOrWhiteSpace(phone))
                        await contactPoints.AddAsync(EntityTypes.Location, locationId,
                            new ContactPointUpsertRequest("PHONE", phone, null, ContactPhoneLabel, IsPrimary: true), ct2,
                            enforceOwnerWrite: false); // la importación ya exige locations.create sobre el consignatario que acaba de crear
                    return publicId;
                }, ct);
                record.LocationPublicId = locationPublicId;
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
                tracked.StatusCodeId, ImportBatchStatuses.Confirmed, $"Consignatarios creados: {created}; fallidos: {failed}; omitidos: {skipped}.", ct2);
            tracked.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            // Lote 45 (F-B2): «Recordar este cliente en la plantilla» → la próxima importación lo propone.
            if (req.RememberClient) await templates.RememberDefaultClientAsync(tracked.ImportTemplateId, tracked.ClientId, ct2);
        }, ct);

        return new ConsigneeImportResultDto(batch.PublicId, ImportBatchStatuses.Confirmed, created, failed, skipped, records.Select(r => r.ToDto()).ToList());
    }

    // ================================================================ DESCARTAR

    public async Task<ConsigneeImportPreviewDto> DiscardAsync(Guid batchPublicId, CancellationToken ct)
    {
        var batch = await LoadBatchAsync(batchPublicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var tracked = await db.ImportBatches.FirstOrDefaultAsync(b => b.ImportBatchId == batch.ImportBatchId, ct2)
                          ?? throw new NotFoundException(BatchNotFoundLabel);
            if (!await db.IsInitialAsync(tracked.StatusCodeId, ct2)) throw new StatusRuleException(DiscardOnlyValidatedMessage);
            if (tracked.ConfirmedAtUtc is not null) throw new ConflictException(ConfirmInProgressMessage);
            var to = await statuses.TransitionAsync(ImportBatchStatuses.Domain, ImportEntityTypes.ImportBatch, tracked.ImportBatchId,
                tracked.StatusCodeId, ImportBatchStatuses.Discarded, null, ct2);
            // Mismo candado que la reserva de ConfirmAsync: solo descarta si nadie reservó el lote entretanto.
            var validatedId = tracked.StatusCodeId;
            var locked = await db.ImportBatches
                .Where(b => b.ImportBatchId == tracked.ImportBatchId && b.StatusCodeId == validatedId && b.ConfirmedAtUtc == null)
                .ExecuteUpdateAsync(u => u.SetProperty(b => b.StatusCodeId, to.StatusCodeId), ct2);
            if (locked == 0) throw new ConflictException(ConfirmInProgressMessage);
            tracked.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(batchPublicId, ct);
    }

    // ================================================================ validación fila a fila (sin guardar)

    /// <summary>
    /// Lote 46 (F-B3): fila de una plantilla con variantes: la primera variante cuyas condiciones se cumplen da el mapeo (la fila
    /// lleva su número y nombre); si ninguna aplica, la fila se rechaza con sus celdas a la vista.
    /// </summary>
    private static ConsigneeImportRowRecord ValidateVariantRow(CsvRow row, IReadOnlyList<ImportVariant> variants, IReadOnlyDictionary<string, string> defaults, RowContext ctx)
    {
        var index = ConsigneeImportVariants.Select(row.Cells, variants);
        if (index < 0)
        {
            var rejected = new ConsigneeImportRowRecord { Row = row.RowNumber, Line = row.Line, Cells = row.Cells.Select(c => c.Trim()).ToList() };
            rejected.Errors[ConsigneeImportVariants.RowField] = ConsigneeImportVariants.NoVariantMatchesMessage;
            return rejected;
        }
        var rec = ValidateRow(row, variants[index].Columns, defaults, ctx);
        rec.Variant = index + 1;
        rec.VariantName = ConsigneeImportVariants.DisplayName(variants[index], index);
        return rec;
    }

    private static ConsigneeImportRowRecord ValidateRow(CsvRow row, IReadOnlyList<ImportColumn> columns, IReadOnlyDictionary<string, string> defaults, RowContext ctx)
    {
        var values = ConsigneeImportMapping.Map(row.Cells, columns, defaults);
        var rec = new ConsigneeImportRowRecord { Row = row.RowNumber, Line = row.Line, Values = new Dictionary<string, string>(values, StringComparer.Ordinal) };
        string? Get(string field) => rec.Values.TryGetValue(field, out var v) ? v : null;
        void Error(string field, string message) => rec.Errors.TryAdd(field, message);
        void Max(string field, int max) { if (Get(field) is { } v && v.Length > max) Error(field, ConsigneeImportMapping.MaxLengthMessage(max)); }

        // País: PR si falta
        var country = (Get(ConsigneeImportFields.Country) ?? LocationService.DefaultCountry).ToUpperInvariant();
        rec.Values[ConsigneeImportFields.Country] = country;
        if (!CountryCode.IsValid(country)) Error(ConsigneeImportFields.Country, CountryCode.InvalidMessage);
        else if (!ctx.Countries.Contains(country)) Error(ConsigneeImportFields.Country, ConsigneeImportMapping.UnknownCountryMessage(country));

        // Lote 45 (F-B2): ZIP de PR/EE.UU. que Excel guardó como número (00901 → 901) se rellena a 5 dígitos.
        if (Get(ConsigneeImportFields.PostalCode) is string zip)
            rec.Values[ConsigneeImportFields.PostalCode] = ConsigneeImportMapping.NormalizePostalCode(zip, country);

        // Lote 46 (F-B3): pueblo, estado y ZIP con el catálogo de códigos postales ANTES de exigir los obligatorios (solo países que
        // el catálogo cubre y con un país válido). Cada dato completado o ajustado queda en Inferred / OriginalPostalCode y en un aviso.
        if (!rec.Errors.ContainsKey(ConsigneeImportFields.Country))
        {
            var inference = PostalInference.Infer(ctx.Postal, country, Get(ConsigneeImportFields.City), Get(ConsigneeImportFields.State), Get(ConsigneeImportFields.PostalCode));
            if (inference.IsRejected) Error(inference.ErrorField!, inference.Error!);
            else
            {
                void Set(string field, string? value) { if (value is null) rec.Values.Remove(field); else rec.Values[field] = value; }
                Set(ConsigneeImportFields.City, inference.City);
                Set(ConsigneeImportFields.State, inference.State);
                Set(ConsigneeImportFields.PostalCode, inference.PostalCode);
                if (inference.Inferred.Count > 0) rec.Inferred = inference.Inferred.ToList();
                rec.OriginalPostalCode = inference.OriginalPostalCode;
                rec.Warnings.AddRange(inference.Warnings);
            }
        }

        // Textos obligatorios y topes (los de la ficha del consignatario)
        if (Get(ConsigneeImportFields.Name) is null) Error(ConsigneeImportFields.Name, ConsigneeImportMapping.NameRequiredMessage);
        if (Get(ConsigneeImportFields.Line1) is null) Error(ConsigneeImportFields.Line1, ConsigneeImportMapping.Line1RequiredMessage);
        if (Get(ConsigneeImportFields.City) is null) Error(ConsigneeImportFields.City, ConsigneeImportMapping.CityRequiredMessage);
        Max(ConsigneeImportFields.Name, LocationService.NameMax);
        Max(ConsigneeImportFields.Line1, LocationService.LineMax);
        Max(ConsigneeImportFields.Line2, LocationService.LineMax);
        Max(ConsigneeImportFields.City, LocationService.CityMax);
        Max(ConsigneeImportFields.State, LocationService.StateMax);
        Max(ConsigneeImportFields.PostalCode, LocationService.PostalMax);
        Max(ConsigneeImportFields.Code, LocationService.CodeMax);
        Max(ConsigneeImportFields.AccessNotes, LocationService.NotesMax);
        Max(ConsigneeImportFields.DeliveryNotes, LocationService.NotesMax);
        Max(ConsigneeImportFields.ContactName, LocationService.ContactNameMax);

        // Minutos de servicio
        if (Get(ConsigneeImportFields.DefaultServiceMinutes) is string minutesText)
        {
            if (!ImportMapping.TryParseInt(minutesText, out var minutes)) Error(ConsigneeImportFields.DefaultServiceMinutes, ConsigneeImportMapping.ServiceMinutesIntegerMessage);
            else if (minutes < 0) Error(ConsigneeImportFields.DefaultServiceMinutes, ConsigneeImportMapping.ServiceMinutesNegativeMessage);
            else rec.Values[ConsigneeImportFields.DefaultServiceMinutes] = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // Ventana horaria
        TimeOnly? ParseTime(string field)
        {
            if (Get(field) is not string text) return null;
            if (ConsigneeImportMapping.TryParseTime(text, out var t)) { rec.Values[field] = t.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture); return t; }
            Error(field, ConsigneeImportMapping.TimeFormatMessage);
            return null;
        }
        var start = ParseTime(ConsigneeImportFields.DefaultWindowStart);
        var end = ParseTime(ConsigneeImportFields.DefaultWindowEnd);
        if (!rec.Errors.ContainsKey(ConsigneeImportFields.DefaultWindowStart) && !rec.Errors.ContainsKey(ConsigneeImportFields.DefaultWindowEnd)
            && ConsigneeImportMapping.ValidateWindow(start, end) is (string windowField, string windowMessage))
            Error(windowField, windowMessage);

        // Factura repetida permitida
        if (Get(ConsigneeImportFields.AllowDupInvoice) is string allowText)
        {
            if (ConsigneeImportMapping.TryParseBool(allowText, out var allow)) rec.Values[ConsigneeImportFields.AllowDupInvoice] = allow ? "true" : "false";
            else Error(ConsigneeImportFields.AllowDupInvoice, ConsigneeImportMapping.AllowDupInvoiceMessage);
        }

        // Teléfono: misma validación que ContactPointService (se guarda como ContactPoint PHONE principal al confirmar)
        // Lote 45 (F-B2): formatos mezclados → 10 dígitos; dos números en la celda → el primero, con aviso.
        if (Get(ConsigneeImportFields.ContactPhone) is string rawPhone)
        {
            var (phoneText, multiple) = ConsigneeImportMapping.NormalizePhone(rawPhone);
            if (multiple) rec.Warnings.Add(ConsigneeImportMapping.MultiplePhonesWarning(phoneText));
            try { rec.Values[ConsigneeImportFields.ContactPhone] = ContactPointService.ValidateValue("PHONE", phoneText); }
            catch (ValidationException ex) { Error(ConsigneeImportFields.ContactPhone, ex.Message); }
        }

        ResolveOwner(rec, ctx);
        return rec;
    }

    /// <summary>
    /// Dueño: clientCode, luego clientName, luego el cliente del lote; si ninguno, compartida. Lote 45 (F-B2): el cliente del
    /// lote es el «cliente por defecto»: una fila que trae su propio cliente válido y activo queda con ESE cliente aunque sea otro
    /// (antes era error 'La fila indica el cliente…'); un archivo puede traer consignatarios de varios clientes.
    /// </summary>
    private static void ResolveOwner(ConsigneeImportRowRecord rec, RowContext ctx)
    {
        string? Get(string field) => rec.Values.TryGetValue(field, out var v) ? v : null;
        ClientRow? rowClient = null;
        string? field = null;
        if (Get(ConsigneeImportFields.ClientCode) is string code)
        {
            field = ConsigneeImportFields.ClientCode;
            rowClient = ctx.Clients.Where(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.IsActive).ThenBy(c => c.ClientId).FirstOrDefault();
            if (rowClient is null) { rec.Errors.TryAdd(field, ClientCodeNotFoundMessage(code)); return; }
        }
        else if (Get(ConsigneeImportFields.ClientName) is string name)
        {
            field = ConsigneeImportFields.ClientName;
            var matches = ctx.Clients.Where(c => string.Equals(c.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0) { rec.Errors.TryAdd(field, ClientNameNotFoundMessage(name)); return; }
            if (matches.Count > 1) { rec.Errors.TryAdd(field, ClientNameAmbiguousMessage(name)); return; }
            rowClient = matches[0];
        }

        if (rowClient is not null && field is not null && !rowClient.IsActive) { rec.Errors.TryAdd(field, ClientQueries.ClientInactiveMessage); return; }

        var owner = rowClient ?? ctx.BatchClient;
        rec.OwnerResolved = true;
        rec.OwnerClientId = owner?.ClientId;
        rec.OwnerClientPublicId = owner?.PublicId;
        rec.OwnerClientName = owner?.Name;
    }

    /// <summary>Clave de la fila para las reglas de duplicados (con onlyValidFields, omite los campos que ya traen error).</summary>
    private static ConsigneeRowKey KeyOf(ConsigneeImportRowRecord r, bool onlyValidFields)
    {
        string? Get(string field) => (!onlyValidFields || !r.Errors.ContainsKey(field)) && r.Values.TryGetValue(field, out var v) ? v : null;
        return new ConsigneeRowKey(r.Row, r.OwnerClientId, Get(ConsigneeImportFields.Code), Get(ConsigneeImportFields.Name), Get(ConsigneeImportFields.Line1));
    }

    private static void Apply(ConsigneeImportRowRecord r, IEnumerable<DuplicateFinding> findings)
    {
        foreach (var f in findings)
        {
            if (f.IsWarning) { if (!r.Warnings.Contains(f.Message)) r.Warnings.Add(f.Message); }
            else r.Errors.TryAdd(f.Field, f.Message);
        }
    }

    private static void ApplyFileDuplicates(List<ConsigneeImportRowRecord> records)
    {
        var findings = ConsigneeImportMapping.FindDuplicatesInFile(records.Where(r => r.OwnerResolved).Select(r => KeyOf(r, onlyValidFields: true)));
        foreach (var r in records)
            if (findings.TryGetValue(r.Row, out var list)) Apply(r, list);
    }

    // ================================================================ directorio y carga

    /// <summary>
    /// Consignatarios que pueden chocar con las filas: los compartidos, los de los clientes dueños de alguna fila y, para las
    /// filas compartidas con código, los de cualquier cliente con ese código (aviso). afterLocationId &gt; 0 trae solo los
    /// creados después (refresco incremental del confirmar).
    /// </summary>
    private async Task<List<ExistingConsignee>> LoadDirectoryAsync(IReadOnlyCollection<ConsigneeImportRowRecord> records, int afterLocationId, CancellationToken ct)
    {
        var owners = records.Where(r => r.OwnerResolved && r.OwnerClientId is not null).Select(r => r.OwnerClientId).Distinct().ToList();
        var sharedCodes = records
            .Where(r => r.OwnerResolved && r.OwnerClientId is null && r.Values.TryGetValue(ConsigneeImportFields.Code, out var c) && !string.IsNullOrWhiteSpace(c))
            .Select(r => r.Values[ConsigneeImportFields.Code].Trim().ToUpperInvariant())
            .Distinct().ToList();
        var anyShared = sharedCodes.Count > 0;
        var deliveryId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Delivery, ct);
        var bothId = await lookups.TryGetIdAsync(LookupDomains.LocationType, LocationTypes.Both, ct);

        return await db.Locations.AsNoTracking()
            .Where(l => l.LocationId > afterLocationId
                        && (l.ClientId == null
                            || owners.Contains(l.ClientId)
                            || (anyShared && l.Code != null && sharedCodes.Contains(l.Code.ToUpper()))))
            .Select(l => new ExistingConsignee(l.LocationId, l.ClientId, l.Code, l.Name, l.Line1, l.IsActive,
                l.LocationTypeLookupId == deliveryId || l.LocationTypeLookupId == bothId))
            .ToListAsync(ct);
    }

    /// <summary>Países activos, sin los que el tenant deshabilitó con su override (como la captura y el importador de órdenes).</summary>
    private async Task<IReadOnlySet<string>> ActiveCountriesAsync(CancellationToken ct)
    {
        var disabled = (await db.LookupCodeOverrides.AsNoTracking().Where(o => !o.IsEnabled).Select(o => o.LookupCodeId).ToListAsync(ct)).ToHashSet();
        return (await lookups.GetDomainAsync(LookupDomains.Country, ct))
            .Where(l => l.IsActive && !disabled.Contains(l.LookupCodeId))
            .Select(l => l.InternalCode.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<ImportBatch> LoadBatchAsync(Guid publicId, CancellationToken ct)
        => await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.PublicId == publicId && b.Kind == ImportKinds.Consignee, ct)
           ?? throw new NotFoundException(BatchNotFoundLabel);

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

    /// <summary>Sin rows: todas las válidas (400 si no hay ninguna). Con rows: deben existir y ser válidas (400 por fila).</summary>
    private static HashSet<int> SelectRows(List<ConsigneeImportRowRecord> records, IList<int>? requested)
    {
        if (requested is null || requested.Count == 0)
        {
            var valid = records.Where(r => r.IsValid).Select(r => r.Row).ToHashSet();
            if (valid.Count == 0) throw new ValidationException("rows", NoValidRowsMessage);
            return valid;
        }

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

    /// <summary>Mismo request que POST /locations (tipo DELIVERY); el dueño se pasa aparte a CreateCoreAsync.</summary>
    private static LocationUpsertRequest ToLocationRequest(ConsigneeImportRowRecord r)
    {
        string? Get(string field) => r.Values.TryGetValue(field, out var v) ? v : null;
        var minutes = ImportMapping.TryParseInt(Get(ConsigneeImportFields.DefaultServiceMinutes), out var m) ? m : 0;
        TimeOnly? start = ConsigneeImportMapping.TryParseTime(Get(ConsigneeImportFields.DefaultWindowStart), out var s) ? s : null;
        TimeOnly? end = ConsigneeImportMapping.TryParseTime(Get(ConsigneeImportFields.DefaultWindowEnd), out var e) ? e : null;
        var allowDup = ConsigneeImportMapping.TryParseBool(Get(ConsigneeImportFields.AllowDupInvoice), out var a) && a;
        return new LocationUpsertRequest(
            ClientPublicId: null,
            Code: Get(ConsigneeImportFields.Code),
            Name: Get(ConsigneeImportFields.Name) ?? string.Empty,
            LocationType: LocationTypes.Delivery,
            Line1: Get(ConsigneeImportFields.Line1) ?? string.Empty,
            Line2: Get(ConsigneeImportFields.Line2),
            City: Get(ConsigneeImportFields.City) ?? string.Empty,
            State: Get(ConsigneeImportFields.State),
            PostalCode: Get(ConsigneeImportFields.PostalCode),
            Country: Get(ConsigneeImportFields.Country),
            DefaultServiceMinutes: minutes,
            DefaultWindowStart: start,
            DefaultWindowEnd: end,
            AccessNotes: Get(ConsigneeImportFields.AccessNotes),
            DeliveryNotes: Get(ConsigneeImportFields.DeliveryNotes),
            AllowDupInvoice: allowDup,
            ContactName: Get(ConsigneeImportFields.ContactName));
    }

    private async Task<ConsigneeImportPreviewDto> ToPreviewAsync(ImportBatch batch, CancellationToken ct)
    {
        var status = await StatusCodeOfAsync(batch.StatusCodeId, ct);
        var templateId = batch.ImportTemplateId;
        var template = await db.ImportTemplates.AsNoTracking().Where(t => t.ImportTemplateId == templateId)
            .Select(t => new { t.PublicId, t.Name }).FirstOrDefaultAsync(ct);
        var clientId = batch.ClientId;
        var client = clientId is null
            ? null
            : await db.Clients.AsNoTracking().Where(c => c.ClientId == clientId).Select(c => new { c.PublicId, c.Name }).FirstOrDefaultAsync(ct);
        var rows = DeserializeRows(batch.RowsJson);
        var confirmed = batch.ConfirmedAtUtc is not null;
        return new ConsigneeImportPreviewDto(
            batch.PublicId, status,
            template?.PublicId ?? Guid.Empty, template?.Name ?? string.Empty,
            client?.PublicId, client?.Name,
            batch.FileName, batch.RowCount, batch.ValidRows,
            rows.Select(r => r.ToDto()).ToList(),
            batch.CreatedAtUtc, batch.ConfirmedAtUtc,
            confirmed ? rows.Count(r => r.LocationPublicId is not null) : null,
            confirmed ? rows.Count(r => r.Error is not null) : null);
    }

    private static string SerializeRows(IEnumerable<ConsigneeImportRowRecord> rows) => JsonSerializer.Serialize(rows, RowsJsonOptions);

    private static List<ConsigneeImportRowRecord> DeserializeRows(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? new List<ConsigneeImportRowRecord>()
            : JsonSerializer.Deserialize<List<ConsigneeImportRowRecord>>(json, RowsJsonOptions) ?? new List<ConsigneeImportRowRecord>();
}
