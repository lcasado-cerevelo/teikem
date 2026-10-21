using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.CustomFields;
using Teikem.Domain.Files;
using Teikem.Domain.Fleet;
using Teikem.Domain.Migration.Colavora;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;

namespace Teikem.Infrastructure.Migration.Colavora;

/// <summary>
/// Órdenes (D2: todo el histórico) por lotes de ShipmentID, directo por EF con la auditoría apagada: la orden con sus dos
/// paradas (snapshot del remitente y del consignatario), líneas y piezas (D8), el historial completo con fechas y usuarios
/// originales (D9), los intentos de entrega, un viaje por manifiesto (D5), el comprobante con firma y foto en disco (D10),
/// el precio congelado (D13), la división (D6) y la factura procesada (paperwork back, D4/D8).
/// </summary>
public sealed partial class ColavoraImportService
{
    private async Task ImportOrdersAsync(RunState s, CancellationToken ct)
    {
        var opt = s.Config.Options;
        var (minId, maxId, count) = await s.Reader.ShipmentRangeAsync(s.CompanyId, opt.SinceDate, ct);
        var from = Math.Max(opt.FromShipmentId ?? minId, minId);
        var to = Math.Min(opt.ToShipmentId ?? maxId, maxId);
        s.Report.AddInfo("Órdenes en Colavora", $"{count:N0}{(opt.SinceDate is DateTime sd ? $" creadas desde {sd:yyyy-MM-dd}" : "")} (ShipmentID {minId}–{maxId}); se procesa {from}–{to} en lotes de {opt.BatchSize}");
        if (!s.DryRun && s.TenantId is null) return;
        await LoadDivisionRulesAsync(s, ct);   // las divisiones viejas fuera de lista son error (aunque la fase de campos no haya corrido)

        if (s.TenantId is int tid0)
        {
            foreach (var t in await db.Trips.AsNoTracking().Where(t => t.Code.StartsWith("M-")).Select(t => new { t.Code, t.TripId }).ToListAsync(ct)) s.Trips[t.Code] = t.TripId;
            // números de pieza ya usados en la compañía (índice único): se cargan por lotes mientras avanza (ver EnsurePieceNumbers)
        }
        var withImages = !s.DryRun && !string.IsNullOrWhiteSpace(opt.PodFilesDir);
        if (withImages) Directory.CreateDirectory(opt.PodFilesDir!);
        s.Report.AddInfo("Firmas y fotos", withImages ? $"archivos en {opt.PodFilesDir}" : "no se guardan (sin options.podFilesDir o simulación)");

        var cursor = from;
        long done = 0;
        var started = DateTime.UtcNow;
        while (cursor <= to)
        {
            ct.ThrowIfCancellationRequested();
            var batch = await s.Reader.ReadShipmentsAsync(s.CompanyId, cursor, to, opt.BatchSize, opt.SinceDate, ct);
            if (batch.Count == 0) break;
            var first = batch[0].ShipmentId;
            var last = batch[^1].ShipmentId;
            var pieces = (await s.Reader.ReadPiecesAsync(first, last, ct)).GroupBy(p => p.ShipmentId).ToDictionary(g => g.Key, g => g.ToList());
            var events = (await s.Reader.ReadEventsAsync(first, last, ct)).GroupBy(e => e.ShipmentId).ToDictionary(g => g.Key, g => g.ToList());
            var pods = (await s.Reader.ReadPodsAsync(first, last, withImages, ct)).GroupBy(p => p.ShipmentId).ToDictionary(g => g.Key, g => g.ToList());

            await ImportOrderBatchAsync(s, batch, pieces, events, pods, ct);
            done += batch.Count;
            cursor = last + 1;
            var elapsed = DateTime.UtcNow - started;
            logger.LogInformation("Órdenes: {Done:N0} procesadas (hasta ShipmentID {Last}); {Rate:N0} por minuto.", done, last, elapsed.TotalMinutes > 0 ? done / elapsed.TotalMinutes : 0);
            if (batch.Count < opt.BatchSize && last >= to) break;
        }
        s.Report.AddInfo("Órdenes procesadas", $"{done:N0} en {(DateTime.UtcNow - started):hh\\:mm\\:ss}");
    }

    private sealed class OrderBuild
    {
        public required CvShipment Src { get; init; }
        public required TransportOrder Order { get; init; }
        public OrderStop? Pickup { get; init; }
        public required OrderStop Delivery { get; init; }
        public List<(DateTime At, int? FromStatus, int ToStatus, string? Comment, int? By, bool Skipped)> History { get; } = new();
        public List<DeliveryAttempt> Attempts { get; } = new();
        public List<(string TripCode, DateTime At, int? By, bool Current)> TripLinks { get; } = new();
        public List<string> Divisions { get; } = new();
        public CvPod? Pod { get; set; }
        public int? PodDriverId { get; set; }
        public DeliveryProof? Proof { get; set; }
        /// <summary>Firma (PNG) y foto (JPEG) decodificadas del POD, para el almacén de archivos (Lote 127).</summary>
        public byte[]? Signature { get; set; }
        public byte[]? Photo { get; set; }
    }

    private async Task ImportOrderBatchAsync(RunState s, List<CvShipment> batch, Dictionary<int, List<CvPiece>> pieces, Dictionary<int, List<CvEvent>> events, Dictionary<int, List<CvPod>> pods, CancellationToken ct)
    {
        const string O = ColavoraEntities.Orders;
        var tenantId = s.TenantId ?? 0;
        var waybills = batch.Select(b => b.Waybill).ToList();
        var existing = s.TenantId is null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : (await db.TransportOrders.AsNoTracking().Where(o => waybills.Contains(o.WaybillNumber)).Select(o => o.WaybillNumber).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await EnsurePieceNumbersLoadedAsync(s, batch, pieces, ct);

        var builds = new List<OrderBuild>();
        var newTrips = new Dictionary<string, Trip>(StringComparer.OrdinalIgnoreCase);
        var newDrivers = new Dictionary<string, Driver>(StringComparer.OrdinalIgnoreCase);
        var seenWaybills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sh in batch)
        {
            s.Report.CountRead(O);
            var key = $"{sh.ShipmentId} {sh.Waybill}";
            if (existing.Contains(sh.Waybill) || !seenWaybills.Add(sh.Waybill)) { s.Report.CountExisting(O); continue; }
            if (!string.IsNullOrWhiteSpace(sh.Division) && s.StaleDivisions.Contains(sh.Division.Trim()))
            {
                s.Report.Reject(O, key, $"La división '{sh.Division.Trim()}' no está en la lista de la compañía y es vieja (sin uso reciente); la orden se reporta como error y no se migra.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(sh.Sender) || !s.Clients.TryGetValue(sh.Sender, out var client))
            {
                s.Report.Reject(O, key, $"El cliente '{sh.Sender}' no está migrado (huérfano o de prueba); la orden se omite.");
                continue;
            }
            try
            {
                var b = BuildOrder(s, sh, client, pieces.GetValueOrDefault(sh.ShipmentId) ?? new(), events.GetValueOrDefault(sh.ShipmentId) ?? new(), pods.GetValueOrDefault(sh.ShipmentId), newTrips, newDrivers);
                builds.Add(b);
                s.Report.CountCreated(O);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                s.Report.Reject(O, key, ex.Message);
            }
        }
        if (s.DryRun || s.TenantId is null || builds.Count == 0) { db.ChangeTracker.Clear(); return; }

        db.SuppressAudit = true;
        try
        {
            // 1) choferes nuevos (vistos solo en eventos) y viajes nuevos
            foreach (var d in newDrivers.Values) db.Drivers.Add(d);
            foreach (var t in newTrips.Values) db.Trips.Add(t);
            // 2) órdenes con paradas, líneas y piezas por navegación
            foreach (var b in builds) db.TransportOrders.Add(b.Order);
            await db.SaveChangesAsync(ct);
            foreach (var (code, d) in newDrivers) s.Drivers[code] = d.DriverId;
            foreach (var (code, t) in newTrips) s.Trips[code] = t.TripId;

            // 3) lo que necesita el id de la orden: historial, intentos, viajes, comprobante, campos
            foreach (var b in builds)
            {
                var oid = b.Order.TransportOrderId;
                // las líneas apuntan a sus paradas (CargoLine no tiene navegación a OrderStop: se llena con los ids ya asignados)
                foreach (var line in b.Order.CargoLines) { line.PickupStopId = b.Pickup?.OrderStopId; line.DeliveryStopId = b.Delivery.OrderStopId; }
                foreach (var (at, fromS, toS, comment, by, skipped) in b.History)
                    db.EntityStatusHistories.Add(new EntityStatusHistory { TenantId = tenantId, EntityTypeLookupId = s.EntityTypeOrderId, EntityId = oid, FromStatusCodeId = fromS, ToStatusCodeId = toS, Comment = comment, ChangedAtUtc = at, ChangedBy = by, IsSkipped = skipped });
                var n = 0;
                foreach (var a in b.Attempts) { a.TransportOrderId = oid; a.AttemptNumber = ++n; db.Set<DeliveryAttempt>().Add(a); }
                foreach (var (code, at, by, current) in b.TripLinks)
                    if (s.Trips.TryGetValue(code, out var tripId))
                        db.TripOrders.Add(new TripOrder { TenantId = tenantId, TripId = tripId, TransportOrderId = oid, IsCurrent = current, AssignedAtUtc = at, AssignedBy = by });
                if (b.Pod is { } pod) AddProof(s, b, pod, oid);
                if (s.DivisionFieldId is int df && b.Divisions.Count > 0)
                    db.CustomFieldValues.Add(new CustomFieldValue { TenantId = tenantId, CustomFieldDefinitionId = df, EntityId = oid, ValueText = b.Divisions[0], UpdatedAtUtc = b.Order.CreatedAtUtc, UpdatedBy = b.Order.CreatedBy });
                if (s.ColavoraIdFieldId is int cf)
                    db.CustomFieldValues.Add(new CustomFieldValue { TenantId = tenantId, CustomFieldDefinitionId = cf, EntityId = oid, ValueNumber = b.Src.ShipmentId, UpdatedAtUtc = b.Order.CreatedAtUtc, UpdatedBy = b.Order.CreatedBy });
            }
            await db.SaveChangesAsync(ct);

            // 4) firma y foto al almacén de archivos (Lote 127, D10): un StoredFile por imagen, dueño = el comprobante recién creado
            await AttachProofFilesAsync(s, builds, ct);
        }
        finally { db.SuppressAudit = false; db.ChangeTracker.Clear(); }
    }

    private bool _storeWarned;

    private async Task AttachProofFilesAsync(RunState s, List<OrderBuild> builds, CancellationToken ct)
    {
        const string E = ColavoraEntities.ProofFiles;
        var pending = builds.Where(b => b.Proof is { DeliveryProofId: > 0 } && (b.Signature is not null || b.Photo is not null)).ToList();
        if (pending.Count == 0) return;
        if (!fileStore.IsConfigured)
        {
            if (!_storeWarned) { s.Report.Warn(E, "almacén", "El almacén de archivos no está configurado (Files:MasterKey): las firmas y fotos no se registran en Teikem (solo quedan en options.podFilesDir si está puesta)."); _storeWarned = true; }
            s.Report.CountSkipped(E, pending.Sum(b => (b.Signature is null ? 0 : 1) + (b.Photo is null ? 0 : 1)));
            return;
        }
        foreach (var b in pending)
        {
            var proofId = b.Proof!.DeliveryProofId;
            var key = $"{b.Src.ShipmentId} {b.Src.Waybill}";
            if (b.Signature is { } sig)
            {
                var dto = await TryAsync(s, E, key, () => storedFiles.AttachAsync(DeliveryProofCodes.EntityType, proofId, StoredFileCodes.KindSignature, "firma.png", null, sig, ct, enforceOwnerWrite: false));
                if (dto is not null) s.Report.CountCreated(E);
            }
            if (b.Photo is { } photo)
            {
                var dto = await TryAsync(s, E, key, () => storedFiles.AttachAsync(DeliveryProofCodes.EntityType, proofId, StoredFileCodes.KindDeliveryPhoto, "foto.jpg", null, photo, ct, enforceOwnerWrite: false));
                if (dto is not null) s.Report.CountCreated(E);
            }
        }
    }

    /// <summary>Carga a la memoria los números de pieza de la compañía que podrían chocar con los del lote (índice único UX_OrderPiece).</summary>
    private async Task EnsurePieceNumbersLoadedAsync(RunState s, List<CvShipment> batch, Dictionary<int, List<CvPiece>> pieces, CancellationToken ct)
    {
        if (s.TenantId is null) return;
        var candidates = batch.SelectMany(b => (pieces.GetValueOrDefault(b.ShipmentId) ?? new()).Select((p, i) => ColavoraRules.PieceNumber(p.Barcode, b.Waybill, i + 1))).Distinct().ToList();
        if (candidates.Count == 0) return;
        foreach (var chunk in candidates.Chunk(1000))
        {
            var found = await db.OrderPieces.AsNoTracking().Where(p => chunk.Contains(p.PackageNumber)).Select(p => p.PackageNumber).ToListAsync(ct);
            foreach (var f in found) s.PieceNumbers.Add(f);
        }
    }

    private OrderBuild BuildOrder(RunState s, CvShipment sh, ClientRef client, List<CvPiece> pieces, List<CvEvent> events, List<CvPod>? pods,
        Dictionary<string, Trip> newTrips, Dictionary<string, Driver> newDrivers)
    {
        const string P = ColavoraEntities.Pieces;
        var tenantId = s.TenantId ?? 0;
        var createdUtc = ColavoraRules.ToUtc(sh.CreatedDate) ?? s.MigrationUtc;
        var createdBy = UserOf(s, sh.CreatedUserId);
        var serviceType = sh.ServiceType is int sti && s.ServiceTypes.TryGetValue(sti, out var st) ? st : (s.DefaultServiceTypeId, "—", false);
        // cliente «Recogido/Devolución» unido a su cliente real: la orden se trabaja como recogido (servicio Pick up) o como devolución (referencia y nota)
        ColavoraRules.SubClientLink? subLink = null;
        if (sh.Sender is not null) s.SubClients.TryGetValue(sh.Sender, out subLink);
        if (subLink?.Kind == ColavoraRules.SubClientKind.Pickup && s.PickupServiceTypeId > 0 && !serviceType.Item3) serviceType = (s.PickupServiceTypeId, "PICK UP", true);
        var codCode = ColavoraRules.MapCodType(sh.CodType, sh.CodAmount);
        var codAmount = sh.CodAmount is > 0 ? sh.CodAmount : null;

        // estatus final (D3) y cuerpo de la orden
        var finalCode = ColavoraRules.MapStatus(sh.Status, createdUtc, s.MigrationUtc, s.Config.Options.PendingCloseDays, s.Config.Options.OpenOrderDays);
        var order = new TransportOrder
        {
            TenantId = tenantId, ClientId = client.ClientId, ContractId = client.ContractId,
            OrderNumber = sh.Waybill, PackBatchNumber = sh.Waybill, WaybillNumber = sh.Waybill,
            ClientInvoiceNumber = string.Empty, ClientInvoiceNumberTyped = false,
            ServiceTypeLookupId = serviceType.Item1, StatusCodeId = s.OrderStatus[OrderStatuses.Submitted],
            RequestedDate = ColavoraRules.ToUtc(sh.ReadyDate), PromisedDate = ColavoraRules.ToUtc(sh.DueDate),
            CodTypeLookupId = codAmount is null ? null : s.CodTypes.GetValueOrDefault(codCode, s.CodTypes["CASH"]), CodAmount = codAmount,
            Notes = ColavoraRules.Trim(subLink?.Kind == ColavoraRules.SubClientKind.Return ? ("Devolución. " + sh.OrderNotes).TrimEnd() : sh.OrderNotes, 2000), IsActive = true, CreatedAtUtc = createdUtc, CreatedBy = createdBy,
        };
        var b = new OrderBuild { Src = sh, Order = order, Pickup = BuildPickupStop(s, sh, client, order), Delivery = BuildDeliveryStop(s, sh, client, order) };
        order.Stops.Add(b.Pickup!);
        order.Stops.Add(b.Delivery);

        // piezas (D8 + P11, 2026-10-19): la factura no es pieza (→ número de factura); los tipos de descripción (ítem de AxisCare) van
        // como descripción y cantidad de la línea de carga, sin pieza ni label; todo lo demás es pieza con su label como número
        var physical = new List<(CvPiece Piece, int Seq)>();
        var descriptions = new List<(string Text, string? Code)>();
        var seq = 0;
        foreach (var p in pieces)
        {
            s.Report.CountRead(P);
            var pt = p.PackageType is int pti && s.PackageTypes.TryGetValue(pti, out var ptv) ? ptv : (s.DefaultPackageTypeId, "Box", false);
            if (pt.Item3)
            {
                if (order.ClientInvoiceNumber.Length == 0 && !string.IsNullOrWhiteSpace(p.Barcode)) { order.ClientInvoiceNumber = ColavoraRules.Trim(p.Barcode, 40)!; order.ClientInvoiceNumberTyped = true; }
                s.Report.CountSkipped(P);
                continue;
            }
            if (p.PackageType is int dpt && s.DescriptionPackageTypes.Contains(dpt))
            {
                descriptions.Add((ColavoraRules.Trim(p.Comments, 200) ?? ColavoraRules.Trim(p.Barcode, 60) ?? pt.Item2, ColavoraRules.Trim(p.Barcode, 60)));
                s.Report.CountSkipped(P);
                continue;
            }
            physical.Add((p, ++seq));
        }
        // lo que va dentro (los ítems): NO son líneas aparte; van como texto en la descripción de la línea del paquete (decisión de Luis, 2026-10-20)
        var contentText = ColavoraRules.ContentText(descriptions);
        if (order.ClientInvoiceNumber.Length == 0 && !string.IsNullOrWhiteSpace(sh.ReferenceNo)) { order.ClientInvoiceNumber = ColavoraRules.Trim(sh.ReferenceNo, 40)!; order.ClientInvoiceNumberTyped = true; }
        if (physical.Count == 0)
        {
            // sin piezas en Colavora: una línea con Qty piezas numeradas waybill-n
            var pt = sh.PackageType is int pti && s.PackageTypes.TryGetValue(pti, out var ptv) ? ptv : (s.DefaultPackageTypeId, "Box", false);
            var qty = Math.Max(1, Math.Min(sh.Qty ?? 1, 200));
            var line = new CargoLine { Order = order, PackageTypeLookupId = pt.Item1, Description = contentText ?? ColavoraRules.LineDescription(null, pt.Item2), Quantity = qty, IsActive = true };
            order.CargoLines.Add(line);
            for (var i = 1; i <= qty; i++) AddPiece(s, order, line, ColavoraRules.PieceNumber(null, sh.Waybill, i), i, 0);
        }
        else
        {
            var firstGroup = true;
            foreach (var g in physical.GroupBy(x => x.Piece.PackageType ?? 0))
            {
                var pt = s.PackageTypes.TryGetValue(g.Key, out var ptv) ? ptv : (s.DefaultPackageTypeId, "Box", false);
                var firstComment = g.Select(x => ColavoraRules.CleanComment(x.Piece.Comments)).FirstOrDefault(c => c is not null);
                // el contenido va en la línea del primer tipo de paquete (con su comentario, si trae uno)
                var description = firstGroup && contentText is not null
                    ? (firstComment is null ? contentText : firstComment + "\n" + contentText)
                    : ColavoraRules.LineDescription(firstComment, pt.Item2);
                firstGroup = false;
                var line = new CargoLine { Order = order, PackageTypeLookupId = pt.Item1, Description = description, Quantity = g.Count(), IsActive = true };
                order.CargoLines.Add(line);
                foreach (var (piece, n) in g) AddPiece(s, order, line, ColavoraRules.PieceNumber(piece.Barcode, sh.Waybill, n), n, piece.PieceId);
            }
        }
        order.TotalPieces = order.CargoLines.Where(l => l.PackageNumber is not null).Sum(l => (int)l.Quantity);
        // Lote 129: zona de entrega por el ZIP del consignatario
        if (ColavoraRules.NormalizeZip(sh.Zip) is { } dzip && s.ZoneIdByZip.TryGetValue(dzip, out var zoneId) && zoneId > 0) order.DeliveryZoneId = zoneId;

        // referencias
        if (!string.IsNullOrWhiteSpace(sh.ReferenceNo)) order.References.Add(new OrderReference { Order = order, RefTypeLookupId = s.RefTypeOtherId, RefValue = ColavoraRules.Trim(sh.ReferenceNo, 100)!, Source = "Colavora ReferenceNo" });
        if (!string.IsNullOrWhiteSpace(sh.PoNumber)) order.References.Add(new OrderReference { Order = order, RefTypeLookupId = s.RefTypePoId, RefValue = ColavoraRules.Trim(sh.PoNumber, 100)!, Source = "Colavora PONumber" });
        if (!string.IsNullOrWhiteSpace(sh.Division)) b.Divisions.Add(sh.Division.Trim());

        // historial, intentos, viajes y precio desde los eventos (D9: todos)
        ApplyEvents(s, b, events, finalCode, newTrips, newDrivers);

        // comprobante (D10): el último POD de la orden
        if (pods is { Count: > 0 }) { b.Pod = pods.OrderByDescending(p => p.CreatedDate).First(); b.PodDriverId = order.StatusCodeId == s.OrderStatus[OrderStatuses.Delivered] ? b.Attempts.LastOrDefault(a => a.Delivered)?.DriverId : null; }

        // paradas: completadas si la orden terminó
        var finalIsDelivered = order.StatusCodeId == s.OrderStatus[OrderStatuses.Delivered];
        var finalIsClosed = finalIsDelivered || order.StatusCodeId == s.OrderStatus[OrderStatuses.Cancelled] || order.StatusCodeId == s.OrderStatus[OrderStatuses.Returned];
        var deliveredAt = b.History.LastOrDefault(h => h.ToStatus == s.OrderStatus[OrderStatuses.Delivered]).At;
        b.Delivery.StatusCodeId = s.StopStatus[finalIsDelivered ? StopStatuses.Completed : finalIsClosed ? StopStatuses.Failed : StopStatuses.Pending];
        b.Delivery.CompletedAtUtc = finalIsDelivered ? (deliveredAt == default ? s.MigrationUtc : deliveredAt) : null;
        var pickedAt = b.History.FirstOrDefault(h => h.ToStatus == s.OrderStatus[OrderStatuses.Pickup] || h.ToStatus == s.OrderStatus[OrderStatuses.Unassigned]).At;
        var pickedUp = pickedAt != default || finalIsClosed;
        b.Pickup!.StatusCodeId = s.StopStatus[pickedUp ? StopStatuses.Completed : StopStatuses.Pending];
        b.Pickup.CompletedAtUtc = pickedUp ? (pickedAt == default ? createdUtc : pickedAt) : null;
        if (codAmount is not null) order.CodStatusCodeId = s.CodStatus.GetValueOrDefault(finalIsDelivered ? CodStatuses.Collected : CodStatuses.Pending);
        return b;
    }

    private void AddPiece(RunState s, TransportOrder order, CargoLine line, string number, int sequence, int colavoraPieceId)
    {
        const string P = ColavoraEntities.Pieces;
        if (!s.PieceNumbers.Add(number))
        {
            var alt = ColavoraRules.PieceNumberAlternate(number, colavoraPieceId > 0 ? colavoraPieceId : sequence);
            s.Report.Warn(P, number, "El código de barras ya existe como pieza de otra orden de la compañía; la pieza queda con sufijo ~id.");
            number = alt;
            s.PieceNumbers.Add(number);
        }
        line.PackageNumber ??= number;
        var piece = new OrderPiece { TenantId = order.TenantId, Order = order, Line = line, Sequence = sequence, PackageNumber = number, IsActive = true, CreatedAtUtc = order.CreatedAtUtc };
        db.OrderPieces.Add(piece);
        s.Report.CountCreated(P);
    }

    private OrderStop BuildPickupStop(RunState s, CvShipment sh, ClientRef client, TransportOrder order)
    {
        var zip = ColavoraRules.NormalizeZip(sh.SenderZip);
        var line1 = ColavoraRules.FirstNonEmpty(sh.SenderAddress1, sh.SenderAddress2, sh.SenderName, client.Name);
        return new OrderStop
        {
            Order = order, StopTypeLookupId = s.StopTypePickupId, Sequence = 1, LocationId = client.PickupLocationId, StatusCodeId = s.StopStatus[StopStatuses.Pending],
            SnapName = ColavoraRules.Trim(sh.SenderName, 200) ?? client.Name, SnapLine1 = ColavoraRules.Trim(line1, 200)!,
            SnapLine2 = string.Equals(line1, sh.SenderAddress2?.Trim(), StringComparison.Ordinal) ? null : ColavoraRules.Trim(sh.SenderAddress2, 200),
            SnapCity = zip is not null && s.CityByZip.TryGetValue(zip, out var city) ? city : "—", SnapState = "PR", SnapPostalCode = zip, SnapCountryCode = "PR",
            Notes = ColavoraRules.Trim(JoinNotes(sh.SenderContact, sh.SenderPhone), 500),
        };
    }

    private OrderStop BuildDeliveryStop(RunState s, CvShipment sh, ClientRef client, TransportOrder order)
    {
        var zip = ColavoraRules.NormalizeZip(sh.Zip);
        var line1 = ColavoraRules.FirstNonEmpty(sh.AddressLine1, sh.AddressLine2, sh.ConsigneeName, "Sin dirección");
        int? locationId = !string.IsNullOrWhiteSpace(sh.ConsigneeNum) && s.ConsigneesByNumber.TryGetValue((client.ClientId, sh.ConsigneeNum.Trim()), out var lid) ? lid : null;
        return new OrderStop
        {
            Order = order, StopTypeLookupId = s.StopTypeDeliveryId, Sequence = 2, LocationId = locationId, StatusCodeId = s.StopStatus[StopStatuses.Pending],
            SnapName = ColavoraRules.Trim(sh.ConsigneeName, 200) ?? ColavoraRules.Trim(sh.ContactPerson, 200), SnapLine1 = ColavoraRules.Trim(line1, 200)!,
            SnapLine2 = string.Equals(line1, sh.AddressLine2?.Trim(), StringComparison.Ordinal) ? null : ColavoraRules.Trim(sh.AddressLine2, 200),
            SnapCity = zip is not null && s.CityByZip.TryGetValue(zip, out var city) ? city : "—", SnapState = "PR", SnapPostalCode = zip, SnapCountryCode = "PR",
            Notes = ColavoraRules.Trim(JoinNotes(sh.ContactPerson, sh.Phone), 500),
        };
    }

    private static string? JoinNotes(string? contact, string? phone)
    {
        var parts = new[] { ColavoraRules.Trim(contact, 150), ColavoraRules.Trim(phone, 50) }.Where(p => p is not null);
        var s = string.Join(" · ", parts);
        return s.Length == 0 ? null : s;
    }

    /// <summary>Recorre los eventos en orden y arma el historial de estatus (con el cierre de la migración al final), los intentos, los viajes y el precio.</summary>
    private void ApplyEvents(RunState s, OrderBuild b, List<CvEvent> events, string? finalCode, Dictionary<string, Trip> newTrips, Dictionary<string, Driver> newDrivers)
    {
        const string H = ColavoraEntities.History;
        var order = b.Order;
        var submitted = s.OrderStatus[OrderStatuses.Submitted];
        int? current = null;
        var ordered = events.Where(e => e.CreatedDate is not null).OrderBy(e => e.CreatedDate).ThenBy(e => e.EventId).ToList();
        var bornAt = ordered.Count > 0 ? Min(order.CreatedAtUtc, ColavoraRules.ToUtc(ordered[0].CreatedDate!.Value)) : order.CreatedAtUtc;
        b.History.Add((bornAt, null, submitted, null, order.CreatedBy, false));
        current = submitted;
        int? currentDriverId = null;
        string? lastManifest = null;
        decimal price = 0;
        DateTime? priceAt = null;
        DateTime? paperworkAt = null;
        int? paperworkBy = null;

        foreach (var e in ordered)
        {
            s.Report.CountRead(H);
            var at = ColavoraRules.ToUtc(e.CreatedDate!.Value);
            var by = UserOf(s, e.CreatedUser);
            var c = ColavoraRules.Classify(e.EventType, e.Event, e.CodAmount);
            var comment = ColavoraRules.Trim(c.Comment, 500);
            switch (c.Kind)
            {
                case ColavoraRules.EventKind.Status:
                    Move(c.TargetStatus!, at, comment, by);
                    break;
                case ColavoraRules.EventKind.AssignDelivery:
                    currentDriverId = ResolveDriver(s, c.DriverCode, c.DriverName, newDrivers) ?? currentDriverId;
                    if (ColavoraRules.ParseManifest(lastManifest) is { } pm && c.DriverCode is not null && !string.Equals(pm.DriverCode, c.DriverCode.TrimStart('0'), StringComparison.OrdinalIgnoreCase))
                        lastManifest = null;   // el manifiesto anterior era de otro chofer: el siguiente Manifest-DL lo repone
                    Move(OrderStatuses.Assigned, at, comment, by, onlyForward: true);
                    break;
                case ColavoraRules.EventKind.ManifestDelivery:
                {
                    var manifest = (e.Event ?? string.Empty).Trim();
                    if (ColavoraRules.ParseManifest(manifest) is { } m)
                    {
                        lastManifest = manifest;
                        var code = ColavoraRules.TripCode(manifest);
                        var driverId = ResolveDriver(s, m.DriverCode, null, newDrivers) ?? currentDriverId;
                        if (!s.Trips.ContainsKey(code) && !newTrips.ContainsKey(code))
                            newTrips[code] = new Trip
                            {
                                TenantId = order.TenantId, Code = code, PlanDate = m.Date, DriverId = driverId,
                                StatusCodeId = s.TripStatus.GetValueOrDefault(m.Date < ColavoraRules.LocalDate(s.MigrationUtc) ? TripStatuses.Completed : TripStatuses.Dispatched, s.TripStatus.Values.First()),
                                PlannedStartUtc = ColavoraRules.ToUtc(m.Date.ToDateTime(new TimeOnly(7, 0))), ActualStartUtc = at, IsActive = true, CreatedAtUtc = at, CreatedBy = by,
                            };
                        foreach (var i in Enumerable.Range(0, b.TripLinks.Count)) b.TripLinks[i] = b.TripLinks[i] with { Current = false };
                        if (!b.TripLinks.Any(t => t.TripCode == code)) b.TripLinks.Add((code, at, by, true));
                        else b.TripLinks[b.TripLinks.FindIndex(t => t.TripCode == code)] = (code, at, by, true);
                    }
                    Comment(at, comment, by);
                    break;
                }
                case ColavoraRules.EventKind.AssignPickup:
                case ColavoraRules.EventKind.ManifestPickup:
                    Comment(at, comment, by);
                    break;
                case ColavoraRules.EventKind.Delivered:
                {
                    var (who, note) = ColavoraRules.ParseDeliveredText(e.Event);
                    b.Attempts.Add(new DeliveryAttempt { TenantId = order.TenantId, DriverId = currentDriverId, AttemptedAtUtc = at, Delivered = true, Notes = ColavoraRules.Trim(JoinNotes(who, note), 400), Source = DeliveryAttemptSources.Status, RecordedBy = by, IsActive = true, CreatedAtUtc = at });
                    Move(OrderStatuses.Delivered, at, comment, by);
                    break;
                }
                case ColavoraRules.EventKind.FailedAttempt:
                {
                    var (who, note) = ColavoraRules.ParseDeliveredText(e.Event);
                    var reason = c.ReasonCode is not null && s.Reasons.TryGetValue(c.ReasonCode, out var r) && r.LookupId > 0 ? r.LookupId : (int?)null;
                    b.Attempts.Add(new DeliveryAttempt { TenantId = order.TenantId, DriverId = currentDriverId, AttemptedAtUtc = at, Delivered = false, FailureReasonLookupId = reason, Notes = ColavoraRules.Trim(JoinNotes(who, note ?? c.Comment), 400), Source = DeliveryAttemptSources.Status, RecordedBy = by, IsActive = true, CreatedAtUtc = at });
                    Move(OrderStatuses.Failed, at, comment, by);
                    break;
                }
                case ColavoraRules.EventKind.PaperworkBack:
                    paperworkAt ??= at;
                    paperworkBy ??= by;
                    Comment(at, comment, by);
                    break;
                case ColavoraRules.EventKind.Price:
                    if (c.Amount is decimal amt) { price += amt; priceAt = at; }
                    Comment(at, comment, by);
                    break;
                default:
                    Comment(at, comment, by);
                    break;
            }
        }

        // estatus final decidido (D3: Pending viejas → Entregada; estatus basura → el último evento manda)
        if (finalCode is not null && s.OrderStatus.TryGetValue(finalCode, out var finalId) && current != finalId)
        {
            var sourceCode = ColavoraRules.MapStatus(b.Src.Status, DateTime.UnixEpoch, DateTime.UnixEpoch);
            var closedByAge = finalCode == OrderStatuses.Delivered && sourceCode is not null && sourceCode != OrderStatuses.Delivered;
            var closing = closedByAge
                ? $"Cerrada por la migración: {b.Src.Status} en Colavora con más de {s.Config.Options.OpenOrderDays ?? s.Config.Options.PendingCloseDays} días sin cerrarse."
                : $"Estatus de Colavora al migrar: {b.Src.Status}.";
            b.History.Add((s.MigrationUtc, current, finalId, closing, null, false));
            current = finalId;
        }
        order.StatusCodeId = current ?? submitted;
        if (price > 0) { order.QuotedAmount = price; order.QuotedAtUtc = priceAt; }
        if (paperworkAt is not null) { order.InvoiceProcessedAtUtc = paperworkAt; order.InvoiceProcessedBy = paperworkBy ?? s.AdminUserId; }
        if (b.TripLinks.Count == 0 && ColavoraRules.ParseManifest(b.Src.ManifestDl) is { } lastM && b.Src.ManifestDl is { } manifestDl)
        {
            // sin eventos de manifiesto pero la orden trae uno: viaje igual
            var code = ColavoraRules.TripCode(manifestDl);
            var at = ColavoraRules.ToUtc(b.Src.ManifestDlDate) ?? order.CreatedAtUtc;
            if (!s.Trips.ContainsKey(code) && !newTrips.ContainsKey(code))
                newTrips[code] = new Trip { TenantId = order.TenantId, Code = code, PlanDate = lastM.Date, DriverId = ResolveDriver(s, lastM.DriverCode, null, newDrivers), StatusCodeId = s.TripStatus.GetValueOrDefault(TripStatuses.Completed, s.TripStatus.Values.First()), PlannedStartUtc = at, ActualStartUtc = at, IsActive = true, CreatedAtUtc = at };
            b.TripLinks.Add((code, at, null, true));
        }

        void Move(string code, DateTime at, string? comment, int? by, bool onlyForward = false)
        {
            if (!s.OrderStatus.TryGetValue(code, out var to)) { Comment(at, comment, by); return; }
            if (to == current) { Comment(at, comment, by); return; }
            if (onlyForward && current is int cur && cur != submitted && (code == OrderStatuses.Assigned) && (cur == s.OrderStatus[OrderStatuses.Delivered] || cur == s.OrderStatus[OrderStatuses.Cancelled] || cur == s.OrderStatus[OrderStatuses.Returned]))
            { Comment(at, comment, by); return; }   // reasignaciones después de cerrar: solo comentario
            b.History.Add((at, current, to, comment, by, false));
            current = to;
        }

        void Comment(DateTime at, string? comment, int? by)
        {
            if (comment is null) return;
            b.History.Add((at, current, current ?? submitted, comment, by, false));
        }
    }

    private static DateTime Min(DateTime a, DateTime b) => a <= b ? a : b;

    /// <summary>Chofer por código de Colavora; si solo aparece en los eventos (no está en Users), se crea con el nombre del evento.</summary>
    private int? ResolveDriver(RunState s, string? code, string? name, Dictionary<string, Driver> newDrivers)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = ColavoraRules.DriverCodeAlias(code);
        if (s.Drivers.TryGetValue(c, out var id)) return id;
        if (s.Drivers.TryGetValue(code.Trim().ToUpperInvariant(), out id)) return id;
        if (newDrivers.TryGetValue(c, out var pending)) return pending.DriverId == 0 ? null : pending.DriverId;
        if (s.DryRun || s.TenantId is null) return null;
        var active = s.DriverStatus.GetValueOrDefault(DriverStatuses.Active);
        if (active == 0) return null;
        newDrivers[c] = new Driver { TenantId = s.TenantId.Value, EmployeeCode = c.Length > 30 ? c[..30] : c, FullName = ColavoraRules.Trim(ColavoraRules.CleanName(name), 150) ?? $"Chofer {c}", StatusCodeId = active, IsActive = false, CreatedAtUtc = DateTime.UtcNow };
        s.Report.CountCreated(ColavoraEntities.Drivers);
        s.Report.Warn(ColavoraEntities.Drivers, c, "Chofer visto solo en los eventos de Colavora (no está en Users); se crea inactivo con el nombre del evento.");
        return null;   // el id se conoce tras el primer SaveChanges del lote; los intentos de este lote quedan sin chofer
    }

    /// <summary>Comprobante de entrega desde el POD de Colavora; la firma y la foto van a disco (D10) y la ruta relativa queda en el comprobante.</summary>
    private void AddProof(RunState s, OrderBuild b, CvPod pod, int orderId)
    {
        const string E = ColavoraEntities.Proofs;
        var at = ColavoraRules.ToUtc(pod.CreatedDate) ?? b.Order.CreatedAtUtc;
        var by = UserOf(s, pod.CreatedUser) ?? s.AdminUserId;
        var hasSignature = !string.IsNullOrWhiteSpace(pod.Signature);
        var proof = new DeliveryProof
        {
            TenantId = b.Order.TenantId, TransportOrderId = orderId, DeliveredAtUtc = at, RecipientName = ColavoraRules.Trim(pod.Name, 150) ?? "(sin nombre)",
            Notes = pod.Cod is > 0 ? ColavoraRules.Trim($"COD cobrado en Colavora: {pod.Cod:0.00}", 500) : null,
            Source = DeliveryProofCodes.SourceDriverApp, RecordedBy = by, SignedByUserId = by,
            SignatureKind = hasSignature ? DeliveryProofCodes.SignatureRecipient : DeliveryProofCodes.SignatureUser,
            GeoStatus = DeliveryProofCodes.GeoNone, IsActive = true, CreatedAtUtc = at,
        };
        if (decimal.TryParse(pod.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) && decimal.TryParse(pod.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng) && lat != 0 && lng != 0)
        {
            proof.Latitude = Math.Round(lat, 6);
            proof.Longitude = Math.Round(lng, 6);
            proof.GeoStatus = DeliveryProofCodes.GeoOk;
            proof.GeoAccuracy = "GPS";
        }
        // D10 + Lote 127: la firma (PNG) y la foto (JPEG) del POD se decodifican aquí y, con el id del comprobante, van al almacén de
        // archivos de Teikem (StoredFile cifrado; la web las muestra en el comprobante). options.podFilesDir, si está, además las exporta a disco.
        b.Signature = hasSignature ? DecodeBase64(s, b.Src.Waybill, "firma.png", pod.Signature!) : null;
        b.Photo = string.IsNullOrWhiteSpace(pod.Image) ? null : DecodeBase64(s, b.Src.Waybill, "foto.jpg", pod.Image!);
        if (s.Config.Options.PodFilesDir is { } dir)
        {
            var folder = Path.Combine(dir, SafeName(b.Src.Waybill));
            if (b.Signature is { } sig) WriteFile(s, folder, b.Src.Waybill, "firma.png", sig);
            if (b.Photo is { } photo) WriteFile(s, folder, b.Src.Waybill, "foto.jpg", photo);
        }
        b.Proof = proof;
        db.Set<DeliveryProof>().Add(proof);
        s.Report.CountCreated(E);
    }

    private static byte[]? DecodeBase64(RunState s, string waybill, string fileName, string base64)
    {
        try { return Convert.FromBase64String(base64.Trim()); }
        catch (FormatException)
        {
            s.Report.Warn(ColavoraEntities.ProofFiles, waybill, $"El archivo {fileName} del comprobante no es base64 válido; se omite.");
            return null;
        }
    }

    private static void WriteFile(RunState s, string folder, string waybill, string fileName, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, fileName), bytes);
        }
        catch (IOException ex)
        {
            s.Report.Warn(ColavoraEntities.ProofFiles, waybill, $"No se pudo exportar {fileName} a disco: {ex.Message}");
        }
    }

    private static string SafeName(string s) => new(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
}
