using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 38 (F-B): mundo de prueba de la importación de consignatarios sobre WmsFixture (InMemory): agrega el dominio
/// ImportBatchStatus (VALIDATED inicial → CONFIRMED; DISCARDED) y el EntityType IMPORT_BATCH como logistica-db-seed.sql,
/// y registra ImportTemplateService, LocationService, ContactPointService y ConsigneeImportService reales.
/// </summary>
internal static class ConsigneeImportWorld
{
    public static async Task<WmsFixture> CreateAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ImportTemplateService>();
            s.AddSingleton<LocationService>();
            s.AddSingleton<ContactPointService>();
            s.AddSingleton<PostalLocalityService>();
            s.AddSingleton<ConsigneeImportService>();
        });

        // InMemory no aplica el DEFAULT NEWID() de PublicId: se asigna al guardar (como haría SQL Server) para que dos
        // plantillas o lotes no compartan Guid.Empty.
        f.Db.SavingChanges += (_, _) =>
        {
            foreach (var e in f.Db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added))
                if (e.Metadata.FindProperty("PublicId") is { ClrType: var t } p && t == typeof(Guid) && (Guid)e.Property(p.Name).CurrentValue! == Guid.Empty)
                    e.Property(p.Name).CurrentValue = Guid.NewGuid();
        };

        var all = await f.Db.LookupCodes.AsNoTracking().ToListAsync();
        var batchType = new LookupCode
        {
            LookupCodeId = 9001, Entity = LookupDomains.EntityType, InternalCode = ImportEntityTypes.ImportBatch, LabelJson = "{\"es\":\"Lote\"}", IsActive = true,
        };
        f.Db.LookupCodes.Add(batchType);
        all.Add(batchType);
        f.Lookups.Load(all);

        var pipe = f.LookupId(LookupDomains.StageKind, StageKinds.Pipeline);
        var term = f.LookupId(LookupDomains.StageKind, StageKinds.Terminal);
        f.Db.StatusCodes.AddRange(
            Status(9101, ImportBatchStatuses.Validated, pipe, 1, initial: true),
            Status(9102, ImportBatchStatuses.Confirmed, pipe, 2),
            Status(9103, ImportBatchStatuses.Discarded, term, 3));

        // Lote 46 (F-B3): catálogo de códigos postales sintético (la importación deduce pueblo, estado y ZIP de entrega con él).
        var pr = f.LookupId(LookupDomains.Country, "PR");
        var us = f.LookupId(LookupDomains.Country, "US");
        PostalLocality Pr(string city, string zip, string municipality, string? zipClass) => new()
            { City = city, PostalCode = zip, State = "PR", Municipality = municipality, CountryLookupId = pr, ZipClass = zipClass };
        PostalLocality Us(string city, string zip, string state, string? zipClass) => new()
            { City = city, PostalCode = zip, State = state, CountryLookupId = us, ZipClass = zipClass };
        f.Db.Set<PostalLocality>().AddRange(
            Pr("PONCE", "00716", "Ponce", ZipClasses.Standard), Pr("PONCE", "00728", "Ponce", ZipClasses.Standard), Pr("PONCE", "00734", "Ponce", ZipClasses.PoBox),
            Pr("SAN JUAN", "00901", "San Juan", ZipClasses.Standard), Pr("SAN JUAN", "00936", "San Juan", ZipClasses.PoBox),
            Pr("ANASCO", "00610", "Añasco", ZipClasses.Standard), Pr("PENUELAS", "00624", "Peñuelas", ZipClasses.Standard),
            Pr("TOA BAJA", "00949", "Toa Baja", ZipClasses.Standard), Pr("SABANA SECA", "00952", "Toa Baja", ZipClasses.Standard),
            Pr("BAYAMON", "00961", "Bayamón", ZipClasses.Standard), Pr("BAYAMON", "00956", "Bayamón", ZipClasses.Standard), Pr("BAYAMON", "00960", "Bayamón", ZipClasses.PoBox),
            Pr("ANGELES", "00611", "Utuado", ZipClasses.PoBox), Pr("UTUADO", "00641", "Utuado", ZipClasses.Standard),
            Pr("CATANO", "00962", "Cataño", null), Pr("FT BUCHANAN", "00934", "Guaynabo", ZipClasses.Unique),
            Us("BOSTON", "02108", "MA", ZipClasses.Standard), Us("SPRINGFIELD", "62701", "IL", ZipClasses.Standard), Us("SPRINGFIELD", "01103", "MA", ZipClasses.Standard),
            Us("APO", "09001", "AE", ZipClasses.Military), Us("BARROW", "99723", "AK", ZipClasses.PoBox));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return f;
    }

    private static StatusCode Status(int id, string code, int stageKindId, int sort, bool initial = false) => new()
    {
        StatusCodeId = id, Entity = ImportBatchStatuses.Domain, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}",
        StageKindLookupId = stageKindId, SortOrder = sort, IsInitial = initial, IsActive = true,
    };

    /// <summary>Plantilla CONSIGNEE general (o de un cliente) con las columnas dadas, por el servicio real.</summary>
    public static Task<ImportTemplateDto> TemplateAsync(WmsFixture f, string name, string[] fields, Dictionary<string, string>? defaults = null, Guid? clientPublicId = null)
        => f.Get<ImportTemplateService>().CreateAsync(
            new ImportTemplateCreateRequest(name, fields.Select((x, i) => new ImportTemplateColumnDto(i + 1, x)).ToList(), defaults, clientPublicId),
            ImportKinds.Consignee, default);

    public static async Task<Location> AddLocationAsync(WmsFixture f, int? clientId, string? code, string name, string line1, bool active = true)
    {
        var l = new Location
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = clientId, Code = code, Name = name, Line1 = line1, City = "San Juan",
            LocationTypeLookupId = f.LookupId(LookupDomains.LocationType, LocationTypes.Delivery), CountryLookupId = f.LookupId(LookupDomains.Country, "PR"),
            IsActive = active, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Locations.Add(l);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return l;
    }
}

/// <summary>
/// Lote 38 (F-B): validación de la importación de consignatarios con el servicio real (InMemory): dueño por clientCode,
/// clientName ambiguo, cliente de baja, duplicados contra la base, plantilla de otro cliente y lote ORDER. La confirmación usa
/// ExecuteUpdateAsync (no soportado en InMemory): la cubre el smoke.
/// </summary>
public sealed class ConsigneeImportServiceTests
{
    private static readonly string[] Fields = ["code", "name", "line1", "city", "clientCode", "contactPhone", "defaultWindowStart", "defaultWindowEnd"];

    private static async Task<ConsigneeImportPreviewDto> ValidateAsync(WmsFixture f, ImportTemplateDto tpl, string csv, Guid? clientPublicId = null)
        => await f.Get<ConsigneeImportService>().ValidateAsync(new ConsigneeImportValidateRequest(tpl.PublicId, clientPublicId, csv, "consignatarios.csv"), default);

    [Fact]
    public async Task Owner_comes_from_client_code_then_batch_client_else_the_row_is_shared()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var acme = await f.AddClientAsync("ACME");
        var tpl = await ConsigneeImportWorld.TemplateAsync(f, "Base", Fields);

        var preview = await ValidateAsync(f, tpl,
            "code,name,line1,city,clientCode,contactPhone,start,end\n" +
            "C-1,Farmacia Uno,Calle 1,Ponce,acme,787-555-1234,08:00,12:00\n" +
            "C-2,Farmacia Dos,Calle 2,Ponce,,,,\n" +
            "C-3,Farmacia Tres,Calle 3,Ponce,NOEXISTE,,,\n");

        Assert.Equal(ImportBatchStatuses.Validated, preview.Status);
        Assert.Null(preview.ClientPublicId);
        Assert.Equal(3, preview.RowCount);
        Assert.Equal(2, preview.ValidRows);
        var r1 = preview.Rows[0];
        Assert.True(r1.IsValid);
        Assert.Equal(acme.PublicId, r1.Owner!.ClientPublicId);
        Assert.False(r1.Owner.IsShared);
        Assert.Equal("PR", r1.Values["country"]); // país por defecto
        Assert.Equal("08:00", r1.Values["defaultWindowStart"]);
        Assert.True(preview.Rows[1].Owner!.IsShared);
        Assert.Null(preview.Rows[2].Owner);
        Assert.Equal("No existe un cliente con el código 'NOEXISTE'.", preview.Rows[2].Errors["clientCode"]);

        // El lote quedó guardado como CONSIGNEE sin cliente y con su historial VALIDATED.
        var batch = await f.Db.ImportBatches.AsNoTracking().SingleAsync(b => b.PublicId == preview.BatchPublicId);
        Assert.Equal(ImportKinds.Consignee, batch.Kind);
        Assert.Null(batch.ClientId);
        Assert.Equal(preview.BatchPublicId, (await f.Get<ConsigneeImportService>().GetAsync(preview.BatchPublicId, default)).BatchPublicId);
    }

    [Fact]
    public async Task Row_validations_use_the_exact_messages()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var tpl = await ConsigneeImportWorld.TemplateAsync(f, "Base", Fields);
        var preview = await ValidateAsync(f, tpl,
            "code,name,line1,city,clientCode,contactPhone,start,end\n" +
            "A,,,,,abc,12:00,08:00\n" +
            "B,Farmacia,Calle 1,Ponce,,,8am,\n" +
            "C,Farmacia,Calle 2,Ponce,,,08:00,\n");

        var e1 = preview.Rows[0].Errors;
        Assert.Equal("El nombre es obligatorio.", e1["name"]);
        Assert.Equal("La dirección (línea 1) es obligatoria.", e1["line1"]);
        Assert.Equal("La ciudad es obligatoria.", e1["city"]);
        Assert.Equal("Teléfono inválido.", e1["contactPhone"]);
        Assert.Equal("La hora de fin de la ventana debe ser posterior a la de inicio.", e1["defaultWindowEnd"]);
        Assert.Equal("La hora debe tener el formato HH:mm (24 horas).", preview.Rows[1].Errors["defaultWindowStart"]);
        Assert.Equal("La ventana horaria requiere hora de inicio y hora de fin.", preview.Rows[2].Errors["defaultWindowEnd"]);
    }

    [Fact]
    public async Task Ambiguous_client_name_and_inactive_client_are_row_errors()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var a = await f.AddClientAsync("A1");
        var b = await f.AddClientAsync("A2");
        var gone = await f.AddClientAsync("BAJA");
        using (f.AsTenant(WmsFixture.TenantId))
        {
            var ta = await f.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == a.ClientId);
            var tb = await f.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == b.ClientId);
            ta.Name = "Farmacias Unidas";
            tb.Name = "farmacias unidas";
            var tg = await f.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == gone.ClientId);
            tg.IsActive = false;
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
        }
        var tpl = await ConsigneeImportWorld.TemplateAsync(f, "Por nombre", ["name", "line1", "city", "clientName"]);
        var preview = await ValidateAsync(f, tpl,
            "name,line1,city,clientName\n" +
            "Uno,Calle 1,Ponce,Farmacias Unidas\n" +
            "Dos,Calle 2,Ponce,Cliente BAJA\n");

        Assert.Equal("Hay más de un cliente con el nombre 'Farmacias Unidas'; use clientCode.", preview.Rows[0].Errors["clientName"]);
        Assert.Equal("El cliente está dado de baja; solo se consulta su historial.", preview.Rows[1].Errors["clientName"]);
        Assert.Equal(0, preview.ValidRows);
    }

    [Fact]
    public async Task The_batch_client_is_the_default_and_a_row_with_its_own_client_wins()
    {
        // Lote 45 (F-B2): «todo el archivo es del cliente X», salvo la fila que trae su propio cliente (antes era error).
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var acme = await f.AddClientAsync("ACME");
        var otro = await f.AddClientAsync("OTRO");
        var tpl = await ConsigneeImportWorld.TemplateAsync(f, "Base", Fields);
        var preview = await ValidateAsync(f, tpl,
            "code,name,line1,city,clientCode\nC-1,Uno,Calle 1,Ponce,OTRO\nC-2,Dos,Calle 2,Ponce,\n", acme.PublicId);

        Assert.Equal(acme.PublicId, preview.ClientPublicId);
        Assert.Equal(2, preview.ValidRows);
        Assert.Equal(otro.PublicId, preview.Rows[0].Owner!.ClientPublicId);   // la fila manda
        Assert.Equal(acme.PublicId, preview.Rows[1].Owner!.ClientPublicId);   // sin cliente en la fila → el del lote
    }

    [Fact]
    public async Task Duplicates_against_the_directory_and_within_the_file()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var acme = await f.AddClientAsync("ACME");
        await ConsigneeImportWorld.AddLocationAsync(f, acme.ClientId, "C-1", "Farmacia Existente", "Calle 9");
        await ConsigneeImportWorld.AddLocationAsync(f, null, "S-1", "Compartida Vieja", "Calle 8", active: false);
        var tpl = await ConsigneeImportWorld.TemplateAsync(f, "Base", Fields);

        var preview = await ValidateAsync(f, tpl,
            "code,name,line1,city,clientCode\n" +
            "C-1,Nueva,Calle 1,Ponce,ACME\n" +               // código del cliente ya usado → error
            "S-1,Otra,Calle 2,Ponce,\n" +                    // compartida contra compartida inactiva → error de reactivar
            "C-1,Compartida,Calle 3,Ponce,\n" +              // compartida con código de un cliente → solo aviso
            "N-1,Farmacia Existente,calle 9,Ponce,ACME\n" +  // mismo nombre + línea 1 → error
            "N-2,Repetida,Calle 5,Ponce,ACME\n" +
            "N-2,Otra Repetida,Calle 6,Ponce,ACME\n");       // código repetido en el archivo → error con la fila 5

        Assert.Equal("Ya existe un consignatario con el código 'C-1' ('Farmacia Existente').", preview.Rows[0].Errors["code"]);
        Assert.Equal("Ya existe un consignatario inactivo con el código 'S-1' ('Compartida Vieja'); reactívelo en vez de importarlo.", preview.Rows[1].Errors["code"]);
        Assert.True(preview.Rows[2].IsValid);
        Assert.Contains("El código C-1 también lo usa un consignatario de un cliente; para ese cliente seguirá resolviendo al suyo.", preview.Rows[2].Warnings);
        Assert.Equal("Ya existe un consignatario activo con el mismo nombre y dirección (línea 1).", preview.Rows[3].Errors["name"]);
        Assert.True(preview.Rows[4].IsValid);
        Assert.Equal("El código N-2 se repite en la fila 5 del archivo.", preview.Rows[5].Errors["code"]);
        Assert.Equal(2, preview.ValidRows);
    }

    [Fact]
    public async Task Template_of_another_client_or_of_orders_is_a_404_and_inactive_template_a_400()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var acme = await f.AddClientAsync("ACME");
        var otro = await f.AddClientAsync("OTRO");
        var svc = f.Get<ConsigneeImportService>();
        var ofOther = await ConsigneeImportWorld.TemplateAsync(f, "De OTRO", Fields, clientPublicId: otro.PublicId);
        const string csv = "code,name,line1,city\nC-1,Uno,Calle 1,Ponce\n";

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => ValidateAsync(f, ofOther, csv, acme.PublicId));
        Assert.Equal("Plantilla de importación no encontrado.", ex.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => ValidateAsync(f, ofOther, csv)); // sin cliente: solo las generales

        var orderTpl = await f.Get<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest("Órdenes",
            new List<ImportTemplateColumnDto> { new(1, "consigneeName"), new(2, "pieces") }), ImportKinds.Order, default);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.ValidateAsync(new ConsigneeImportValidateRequest(orderTpl.PublicId, null, csv), default));

        var general = await ConsigneeImportWorld.TemplateAsync(f, "General", Fields);
        await f.Get<ImportTemplateService>().DeactivateAsync(general.PublicId, ImportKinds.Consignee, default);
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => ValidateAsync(f, general, csv));
        Assert.Equal("La plantilla está inactiva; reactívela o elija otra.", inactive.Message);
    }

    [Fact]
    public async Task Batch_client_missing_or_inactive_and_empty_content_fail_before_parsing()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var gone = await f.AddClientAsync("BAJA");
        using (f.AsTenant(WmsFixture.TenantId))
        {
            var t = await f.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == gone.ClientId);
            t.IsActive = false;
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
        }
        var tpl = await ConsigneeImportWorld.TemplateAsync(f, "Base", Fields);
        const string csv = "code,name,line1,city\nC-1,Uno,Calle 1,Ponce\n";

        Assert.Equal("El cliente está dado de baja; solo se consulta su historial.",
            (await Assert.ThrowsAsync<ConflictException>(() => ValidateAsync(f, tpl, csv, gone.PublicId))).Message);
        await Assert.ThrowsAsync<NotFoundException>(() => ValidateAsync(f, tpl, csv, Guid.NewGuid()));
        Assert.Equal("Indique el contenido del archivo CSV.", (await Assert.ThrowsAsync<ValidationException>(() => ValidateAsync(f, tpl, " "))).Message);
        Assert.Equal("El archivo no tiene filas de datos.", (await Assert.ThrowsAsync<ValidationException>(() => ValidateAsync(f, tpl, "code,name,line1,city\n"))).Message);
    }

    [Fact]
    public async Task An_order_batch_is_not_visible_as_a_consignee_batch()
    {
        await using var f = await ConsigneeImportWorld.CreateAsync();
        var acme = await f.AddClientAsync("ACME");
        var orderTpl = await f.Get<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest("Órdenes",
            new List<ImportTemplateColumnDto> { new(1, "consigneeName"), new(2, "pieces") }), ImportKinds.Order, default);
        var batch = new ImportBatch
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Kind = ImportKinds.Order, ImportTemplateId = orderTpl.Id, ClientId = acme.ClientId,
            RowCount = 0, ValidRows = 0, StatusCodeId = 9101, RowsJson = "[]", CreatedBy = 1, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.ImportBatches.Add(batch);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => f.Get<ConsigneeImportService>().GetAsync(batch.PublicId, default));
        Assert.Equal("Lote de importación no encontrado.", ex.Message);
    }

    [Fact]
    public void Field_catalog_endpoint_marks_required_and_default_allowed()
    {
        var fields = ConsigneeImportService.GetFields();
        Assert.Equal(18, fields.Count);
        Assert.Equal(new ImportFieldDto("contactName", false, true), fields.Single(x => x.Field == "contactName"));
        Assert.Equal(new ImportFieldDto("name", true, false), fields.Single(x => x.Field == "name"));
        Assert.Equal(new ImportFieldDto("city", false, true), fields.Single(x => x.Field == "city")); // Lote 46: sale del ZIP
        Assert.Equal(new ImportFieldDto("allowDupInvoice", false, true), fields.Single(x => x.Field == "allowDupInvoice"));
        Assert.Equal(new ImportFieldDto("clientCode", false, false), fields.Single(x => x.Field == "clientCode"));
    }
}
