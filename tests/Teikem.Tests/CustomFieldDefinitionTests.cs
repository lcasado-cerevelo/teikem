using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote F-F1 (administración de campos personalizados): reglas del servidor que la pantalla da por hechas.
/// Defectos encontrados al construirla: (1) PUT de valores en una entidad SIN resolver de pertenencia (INVOICE) se aceptaba para
/// cualquier id, existiera o no; (2) una definición podía apuntar a un dominio de ESTATUS como "lista" (sus códigos viven en
/// StatusCode y ningún valor lo cumpliría). Se prueba con el servicio real sobre InMemory.
/// </summary>
public class CustomFieldDefinitionTests
{
    private static async Task<WmsFixture> CreateAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<IOwnedEntityResolver>(sp => new WarehouseOwnedEntityResolver(sp.GetRequiredService<Teikem.Infrastructure.Persistence.TeikemDbContext>()));
            s.AddSingleton<CustomFieldService>();
        });

        // Catálogos como logistica-db-seed.sql: tipos de dato, la entidad INVOICE (sin módulo dueño) y dos dominios de lista.
        var all = await f.Db.LookupCodes.AsNoTracking().ToListAsync();
        var next = 20000;
        LookupCode L(string entity, string code)
        {
            var l = new LookupCode { LookupCodeId = next++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
            f.Db.LookupCodes.Add(l);
            all.Add(l);
            return l;
        }
        foreach (var t in new[] { "TEXT", "NUMBER", "SELECT", "MULTISELECT", "LOOKUP_REF", "BOOL", "DATE", "DATETIME" }) L(LookupDomains.CustomFieldDataType, t);
        L(LookupDomains.EntityType, "INVOICE");
        f.Db.CatalogDomains.AddRange(
            new CatalogDomain { DomainKey = "T1_PRIORIDAD", Scope = CatalogScope.Lookup, TenantId = WmsFixture.TenantId, IsSystem = false, IsActive = true, LabelJson = "{\"es\":\"Prioridad\"}" },
            new CatalogDomain { DomainKey = "OrderStatus", Scope = CatalogScope.Status, IsSystem = true, IsActive = true, LabelJson = "{\"es\":\"Estatus de orden\"}" });
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(all);
        f.Db.ChangeTracker.Clear();
        return f;
    }

    private static CustomFieldDefinitionUpsert Text(string key, string? refEntity = null, string type = "TEXT")
        => new(key, new Dictionary<string, string> { ["es"] = key, ["en"] = key }, null, type, false, false, null, null, refEntity, false, null, null);

    [Fact]
    public async Task Values_on_an_entity_without_resolver_are_rejected_instead_of_accepted_for_any_id()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<CustomFieldService>();
        // Un campo ya definido para INVOICE (p. ej. creado antes de esta regla) no habilita guardar valores de un registro que no se puede verificar.
        var def = f.Lookups.GetIdAsync(LookupDomains.EntityType, "INVOICE").Result;
        f.Db.CustomFieldDefinitions.Add(new Teikem.Domain.CustomFields.CustomFieldDefinition
        {
            TenantId = WmsFixture.TenantId, EntityTypeLookupId = def, FieldKey = "nota", DataTypeLookupId = f.Lookups.GetIdAsync(LookupDomains.CustomFieldDataType, "TEXT").Result,
            LabelJson = "{\"es\":\"Nota\"}", IsActive = true,
        });
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.SetValuesAsync("INVOICE", 999999, new Dictionary<string, object?> { ["nota"] = "x" }, default));
        Assert.Contains("INVOICE", ex.Message);
        Assert.Equal(400, ex.StatusCode);
        Assert.Empty(await f.Db.CustomFieldValues.ToListAsync());
    }

    [Fact]
    public async Task Values_on_a_missing_record_are_404_and_on_an_existing_one_are_saved()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<CustomFieldService>();
        await svc.CreateDefinitionAsync(EntityTypes.Warehouse, Text("nota"), default);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.SetValuesAsync(EntityTypes.Warehouse, 999999, new Dictionary<string, object?> { ["nota"] = "x" }, default));
        Assert.Empty(await f.Db.CustomFieldValues.ToListAsync());

        var w = await f.AddWarehouseAsync("ALM-01");
        var saved = await svc.SetValuesAsync(EntityTypes.Warehouse, w.WarehouseId, new Dictionary<string, object?> { ["nota"] = "hola" }, default);
        Assert.Equal("hola", Assert.Single(saved).Value);
    }

    [Fact]
    public async Task A_definition_cannot_be_created_for_an_entity_without_resolver()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<CustomFieldService>();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateDefinitionAsync("INVOICE", Text("nota"), default));
        Assert.Contains("INVOICE", ex.Message);
        Assert.Contains("entityType", ex.Errors!.Keys);
        Assert.Empty(await f.Db.CustomFieldDefinitions.ToListAsync());
        Assert.Equal("warehouse", (await svc.CreateDefinitionAsync(EntityTypes.Warehouse, Text("warehouse"), default)).FieldKey);
    }

    [Fact]
    public async Task A_list_field_only_accepts_lookup_domains_not_status_domains()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<CustomFieldService>();
        var ok = await svc.CreateDefinitionAsync(EntityTypes.Warehouse, Text("prioridad", "T1_PRIORIDAD", "SELECT"), default);
        Assert.Equal("T1_PRIORIDAD", ok.RefEntity);

        var status = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateDefinitionAsync(EntityTypes.Warehouse, Text("estatus", "OrderStatus", "SELECT"), default));
        Assert.Equal("La lista 'OrderStatus' no existe.", status.Message);
        var missing = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateDefinitionAsync(EntityTypes.Warehouse, Text("otra", "NO_EXISTE", "LOOKUP_REF"), default));
        Assert.Equal("La lista 'NO_EXISTE' no existe.", missing.Message);

        // la edición aplica la misma regla
        var update = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateDefinitionAsync(ok.Id, Text("prioridad", "OrderStatus", "SELECT"), default));
        Assert.Equal("La lista 'OrderStatus' no existe.", update.Message);
    }

    [Fact]
    public async Task Definition_messages_match_the_manual()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<CustomFieldService>();
        Assert.Equal("Clave inválida: minúsculas, dígitos y '_' (2-60), empezando por letra.",
            (await Assert.ThrowsAsync<ValidationException>(() => svc.CreateDefinitionAsync(EntityTypes.Warehouse, Text("Mal-Nombre"), default))).Message);
        await svc.CreateDefinitionAsync(EntityTypes.Warehouse, Text("nota"), default);
        Assert.Equal("Ya existe el campo 'nota' en WAREHOUSE.",
            (await Assert.ThrowsAsync<ConflictException>(() => svc.CreateDefinitionAsync(EntityTypes.Warehouse, Text("nota"), default))).Message);
        Assert.Equal("LOOKUP_REF requiere el dominio de lista referenciado.",
            (await Assert.ThrowsAsync<ValidationException>(() => svc.CreateDefinitionAsync(EntityTypes.Warehouse, Text("ref", null, "LOOKUP_REF"), default))).Message);
        Assert.Equal("Un campo de selección necesita opciones o una lista de valores (refEntity).",
            (await Assert.ThrowsAsync<ValidationException>(() => svc.CreateDefinitionAsync(EntityTypes.Warehouse, Text("sel", null, "SELECT"), default))).Message);
        Assert.Equal("ValidationJson no es JSON válido.",
            (await Assert.ThrowsAsync<ValidationException>(() => svc.CreateDefinitionAsync(EntityTypes.Warehouse, Text("vjson") with { ValidationJson = "{no" }, default))).Message);
        Assert.Equal("La etiqueta es obligatoria.",
            (await Assert.ThrowsAsync<ValidationException>(() => svc.CreateDefinitionAsync(EntityTypes.Warehouse, Text("lab") with { Labels = new Dictionary<string, string>() }, default))).Message);
    }

    [Fact]
    public async Task Changing_the_type_of_a_field_with_values_is_a_conflict_and_deactivating_keeps_the_values()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<CustomFieldService>();
        var def = await svc.CreateDefinitionAsync(EntityTypes.Warehouse, Text("nota"), default);
        var w = await f.AddWarehouseAsync("ALM-02");
        await svc.SetValuesAsync(EntityTypes.Warehouse, w.WarehouseId, new Dictionary<string, object?> { ["nota"] = "hola" }, default);

        Assert.Equal("No se puede cambiar el tipo de un campo que ya tiene valores guardados.",
            (await Assert.ThrowsAsync<ConflictException>(() => svc.UpdateDefinitionAsync(def.Id, Text("nota", null, "NUMBER"), default))).Message);

        await svc.SetDefinitionActiveAsync(def.Id, false, default);
        Assert.Empty(await svc.GetValuesAsync(EntityTypes.Warehouse, w.WarehouseId, default));   // deja de mostrarse…
        Assert.Single(await f.Db.CustomFieldValues.ToListAsync());                                // …pero el valor se conserva
        await svc.SetDefinitionActiveAsync(def.Id, true, default);
        Assert.Equal("hola", Assert.Single(await svc.GetValuesAsync(EntityTypes.Warehouse, w.WarehouseId, default)).Value);
    }
}
