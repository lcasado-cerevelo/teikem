using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-23 (CMS) de punta a punta: el CSV que arma el lector de la hoja de ruta de CMS (varias líneas por orden) pasa por el importador de órdenes con una plantilla que manda la dirección del paciente como
/// PUNTO DE RECOGIDA, un consignatario fijo de CMS (código por defecto de la plantilla) y la ruta como ZONA. Servicios reales sobre InMemory.
/// </summary>
public sealed class CmsOrderImportTests
{
    private static readonly string[] Fields =
    {
        ImportFields.OrderNumber, ImportFields.Waybill, ImportFields.PickupName, ImportFields.PickupLine1, ImportFields.PickupCity, ImportFields.PickupPostalCode,
        ImportFields.RequestedDate, ImportFields.Notes, ImportFields.Zone, ImportFields.Description,
    };

    private static async Task<(OrderSubmissionLifecycleTests.World X, Guid TemplateId, Location Cms)> WorldAsync(bool multiLine = true)
    {
        var x = await OrderSubmissionLifecycleTests.NewWorldAsync(extra: s =>
        {
            s.AddSingleton<ImportTemplateService>();
            s.AddSingleton<ContactPointService>();
            s.AddSingleton<OrderImportService>();
            s.AddSingleton<IEnumerable<IOwnedEntityResolver>>(Array.Empty<IOwnedEntityResolver>());
        });
        var db = x.Db;
        // InMemory no aplica el DEFAULT NEWID() de PublicId: se asigna al guardar, como haría SQL Server
        db.SavingChanges += (_, _) =>
        {
            foreach (var e in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added))
                if (e.Metadata.FindProperty("PublicId") is { ClrType: var t } p && t == typeof(Guid) && (Guid)e.Property(p.Name).CurrentValue! == Guid.Empty)
                    e.Property(p.Name).CurrentValue = Guid.NewGuid();
        };
        db.LookupCodes.AddRange(
            new LookupCode { LookupCodeId = 6000, Entity = LookupDomains.LocationType, InternalCode = LocationTypes.Both, LabelJson = "{\"es\":\"Ambas\"}", IsActive = true },
            new LookupCode { LookupCodeId = 9001, Entity = LookupDomains.EntityType, InternalCode = ImportEntityTypes.ImportBatch, LabelJson = "{\"es\":\"Lote\"}", IsActive = true });
        var pipe = x.F.LookupId(LookupDomains.StageKind, StageKinds.Pipeline);
        var term = x.F.LookupId(LookupDomains.StageKind, StageKinds.Terminal);
        StatusCode St(int id, string code, int kind, int sort, bool initial = false) => new()
            { StatusCodeId = id, Entity = ImportBatchStatuses.Domain, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", StageKindLookupId = kind, SortOrder = sort, IsInitial = initial, IsActive = true };
        db.StatusCodes.AddRange(St(9101, ImportBatchStatuses.Validated, pipe, 1, true), St(9102, ImportBatchStatuses.Confirmed, pipe, 2), St(9103, ImportBatchStatuses.Discarded, term, 3));
        var cms = new Location
        {
            LocationId = 5201, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = x.C.ClientId, Code = "CMS", Name = "Clinical Medical Services",
            LocationTypeLookupId = x.F.LookupId(LookupDomains.LocationType, LocationTypes.Delivery), Line1 = "Carr PR-3 KM 9.5", City = "Carolina",
            CountryLookupId = x.F.LookupId(LookupDomains.Country, "PR"), IsActive = true,
        };
        db.Locations.Add(cms);
        (await db.Clients.AsTracking().SingleAsync(c => c.ClientId == x.C.ClientId)).ClientAssignsOrderNumber = true;   // el número de envío es el Order # de CMS: el cliente lo trae
        db.DispatchZones.Add(new DispatchZone { DispatchZoneId = 7001, TenantId = WmsFixture.TenantId, Code = "ERD1", Name = "ERD Pick Up 1", IsActive = true });
        await db.SaveChangesAsync();
        x.F.Lookups.Load(db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        db.ChangeTracker.Clear();

        var template = await x.F.Get<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest(
            "CMS", Fields.Select((f, i) => new ImportTemplateColumnDto(i + 1, f)).ToList(),
            Defaults: new Dictionary<string, string> { [ImportFields.ConsigneeCode] = "CMS", [ImportFields.ServiceType] = "STANDARD", [ImportFields.PackageType] = "BOX" },
            ClientPublicId: null, Delimiter: ",", HasHeader: false, Kind: ImportKinds.Order, MultiLine: multiLine, LineRules: new List<ImportLineRuleDto>()), ImportKinds.Order, default);
        return (x, template.PublicId, cms);
    }

    private static string Csv(params string[] rows) => string.Join("\n", rows) + "\n";

    private static string Tomorrow => DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd");

    [Fact]
    public async Task Two_orders_with_two_lines_each_are_created_with_the_patient_as_pickup_the_fixed_consignee_and_the_route_as_zone()
    {
        var (x, template, cms) = await WorldAsync();
        var d = Tomorrow;
        var csv = Csv(
            $"202607311567,202607311567,\"Alvarado, Sylvia\",Bo Amelia III,Guaynabo,00696,{d},LLAMAR ANTES | PROVIDE WAIVER,ERD Pick Up 1,HOSPITAL BED",
            $"202607311567,202607311567,\"Alvarado, Sylvia\",Bo Amelia III,Guaynabo,00696,{d},LLAMAR ANTES | PROVIDE WAIVER,ERD Pick Up 1,MOTOR",
            $"202608010001,202608010001,\"Perez, Juan\",Calle Luna 5,Carolina,00985,{d},,ERD Pick Up 1,WHEELCHAIR");
        var service = x.F.Get<OrderImportService>();
        var preview = await service.ValidateAsync(new ImportValidateRequest(template, x.C.PublicId, csv, "ruta.csv"), OrderScope.Any, default);
        Assert.All(preview.Rows, r => Assert.Empty(r.Errors));
        Assert.Equal(2, preview.ValidRows);

        // ConfirmAsync reserva el lote con ExecuteUpdate (InMemory no lo traduce): cada fila validada pasa por el mismo mapeo y por OrderService real
        foreach (var row in preview.Rows)
        {
            var rec = new ImportRowRecord { Row = row.Row, Line = row.Line, Values = row.Values.ToDictionary(kv => kv.Key, kv => kv.Value),
                Consignee = new ImportRowConsigneeRecord { Action = row.Consignee!.Action, Name = row.Consignee.Name, LocationPublicId = row.Consignee.LocationPublicId },
                Lines = row.Lines!.Select(l => new ImportLineRecord { Row = l.Row, Kind = l.Kind, Values = l.Values.ToDictionary(kv => kv.Key, kv => kv.Value) }).ToList() };
            await x.Orders.CreateAsync(OrderImportService.ToOrderRequest(rec, x.C, false) with { OverrideCredit = true }, OrderScope.Any, null, default);
            x.Db.ChangeTracker.Clear();
        }

        x.Db.ChangeTracker.Clear();
        var first = await x.Db.TransportOrders.AsNoTracking().Include(o => o.Stops).SingleAsync(o => o.WaybillNumber == "202607311567");
        Assert.Equal(7001, first.DeliveryZoneId);                                   // la ruta fuerza la zona
        Assert.Equal("LLAMAR ANTES | PROVIDE WAIVER", first.Notes);                  // las notas unidas
        Assert.Equal(2, first.TotalPieces);                                         // dos líneas = dos piezas
        var pickup = first.Stops.Single(s => s.Sequence == 1);
        var delivery = first.Stops.Single(s => s.Sequence == 2);
        Assert.Equal("Alvarado, Sylvia", pickup.SnapName);                          // el paciente es la parada de recogida…
        Assert.Equal("Bo Amelia III", pickup.SnapLine1);
        Assert.Equal(cms.LocationId, delivery.LocationId);                          // …y CMS el consignatario fijo
        Assert.Equal(1, await x.Db.Locations.AsNoTracking().CountAsync(l => l.Name == "Alvarado, Sylvia"));
        Assert.Equal(2, await x.Db.TransportOrders.AsNoTracking().CountAsync(o => o.WaybillNumber != null && (o.WaybillNumber == "202607311567" || o.WaybillNumber == "202608010001")));
    }

    [Fact]
    public async Task A_route_that_is_not_a_zone_sends_the_row_to_the_tray_with_a_clear_reason_and_a_half_pickup_says_what_is_missing()
    {
        var (x, template, _) = await WorldAsync();
        var d = Tomorrow;
        var csv = Csv(
            $"1001,1001,Paciente Uno,Calle 1,Ponce,00716,{d},,ERD Pick Up 9,CAMA",
            $"1002,1002,Paciente Dos,,Ponce,00716,{d},,ERD Pick Up 1,CAMA");
        var preview = await x.F.Get<OrderImportService>().ValidateAsync(new ImportValidateRequest(template, x.C.PublicId, csv, "ruta.csv"), OrderScope.Any, default);
        Assert.Equal(0, preview.ValidRows);
        Assert.Equal(OrderService.UnknownZoneMessage("ERD Pick Up 9"), preview.Rows[0].Errors[ImportFields.Zone]);
        Assert.Equal(OrderImportService.PickupFieldRequiredMessage("la dirección (línea 1)"), preview.Rows[1].Errors[ImportFields.PickupLine1]);
    }
}
