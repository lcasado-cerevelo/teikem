using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Tax;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Tax;
using Xunit;

namespace Teikem.Tests;

// Lote 248: el servicio de la exención por temporada — mensajes exactos, datos de otra compañía (404), edición completa mientras está programado con la
// selección reemplazada sin borrar, ya empezado solo la fecha final (409 lo demás), concurrencia y baja/reactivación.
public sealed class TaxExemptionPeriodServiceTests
{
    private static DateOnly Today => TenantClock.Default.Today;

    private static async Task<(WmsFixture F, TaxExemptionPeriodService Svc)> CreateAsync()
    {
        var f = await WmsFixture.CreateAsync();
        f.Db.Set<ProductCategory>().AddRange(
            new ProductCategory { ProductCategoryId = 9101, TenantId = WmsFixture.TenantId, Name = "Escolar" },
            new ProductCategory { ProductCategoryId = 9102, TenantId = WmsFixture.TenantId, ParentId = 9101, Name = "Libretas" },
            new ProductCategory { ProductCategoryId = 9201, TenantId = WmsFixture.OtherTenantId, Name = "Ajena" });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return (f, new TaxExemptionPeriodService(f.Db, f.Tenant, TenantClock.Default));
    }

    private static TaxExemptionPeriodRequest Req(string? name = "Regreso a clases", string? reason = "OA 2026-15", DateOnly? from = null, DateOnly? to = null,
        string? scope = "ALL", Guid[]? products = null, int[]? categories = null)
        => new(name, reason, from ?? Today.AddDays(10), to ?? Today.AddDays(20), scope, products, categories);

    private static async Task<string> Bad(Func<Task> act) => (await Assert.ThrowsAsync<ValidationException>(act)).Message;

    [Fact]
    public async Task Creating_validates_with_the_exact_messages_and_other_companies_targets_are_not_found()
    {
        var (f, svc) = await CreateAsync();
        await using var _ = f;
        Assert.Equal(TaxExemptionPeriodRules.NameRequired, await Bad(() => svc.CreateAsync(Req(name: " "), default)));
        Assert.Equal(TaxExemptionPeriodRules.NameTooLong, await Bad(() => svc.CreateAsync(Req(name: new string('n', 101)), default)));
        Assert.Equal(TaxExemptionPeriodRules.ReasonRequired, await Bad(() => svc.CreateAsync(Req(reason: ""), default)));
        Assert.Equal(TaxExemptionPeriodRules.ReasonTooLong, await Bad(() => svc.CreateAsync(Req(reason: new string('r', 301)), default)));
        Assert.Equal(TaxExemptionPeriodRules.ValidFromRequired, await Bad(() => svc.CreateAsync(Req() with { ValidFrom = null }, default)));
        Assert.Equal(TaxExemptionPeriodRules.ValidToRequired, await Bad(() => svc.CreateAsync(Req() with { ValidTo = null }, default)));
        Assert.Equal(TaxRules.DatesOrder, await Bad(() => svc.CreateAsync(Req(from: Today.AddDays(5), to: Today.AddDays(4)), default)));
        Assert.Equal(TaxExemptionPeriodRules.EndBeforeToday, await Bad(() => svc.CreateAsync(Req(from: Today.AddDays(-9), to: Today.AddDays(-1)), default)));
        Assert.Equal(TaxExemptionPeriodRules.ScopeInvalid, await Bad(() => svc.CreateAsync(Req(scope: "ALGUNOS"), default)));
        Assert.Equal(TaxExemptionPeriodRules.SelectionRequired, await Bad(() => svc.CreateAsync(Req(scope: "SELECTED"), default)));
        Assert.Equal(TaxExemptionPeriodRules.TooManyTargets, await Bad(() => svc.CreateAsync(Req(scope: "SELECTED", categories: Enumerable.Range(1, 201).ToArray()), default)));
        Assert.Equal(TaxExemptionPeriodRules.TooManyTargets, await Bad(() => svc.CreateAsync(Req(scope: "SELECTED",
            products: Enumerable.Range(0, 2001).Select(_ => Guid.NewGuid()).ToArray()), default)));

        var foreign = await f.AddProductAsync("AJENO", tenantId: WmsFixture.OtherTenantId);
        Assert.Equal("Producto no encontrado.", (await Assert.ThrowsAsync<NotFoundException>(() => svc.CreateAsync(Req(scope: "SELECTED", products: new[] { foreign.PublicId }), default))).Message);
        Assert.Equal("Categoría no encontrada.", (await Assert.ThrowsAsync<NotFoundException>(() => svc.CreateAsync(Req(scope: "SELECTED", categories: new[] { 9201 }), default))).Message);
        Assert.Equal(TaxExemptionPeriodRules.NotFound, (await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync(Guid.NewGuid(), default))).Message);
        Assert.Empty(await f.Db.Set<TaxExemptionPeriod>().AsNoTracking().ToListAsync());

        var ok = await svc.CreateAsync(Req(scope: "selected", categories: new[] { 9102 }), default);
        Assert.Equal(("SELECTED", TaxExemptionPeriodStates.Scheduled, true), (ok.Scope, ok.State, ok.IsActive));
        Assert.Equal("Libretas", Assert.Single(ok.Categories).Name);
        Assert.Single(await svc.ListAsync(false, default));
        using (f.AsTenant(WmsFixture.OtherTenantId))
            Assert.Empty(await svc.ListAsync(true, default));
    }

    [Fact]
    public async Task While_scheduled_everything_is_edited_and_the_selection_is_replaced_without_deleting()
    {
        var (f, svc) = await CreateAsync();
        await using var _ = f;
        var a = await f.AddProductAsync("A-1");
        var b = await f.AddProductAsync("B-1");
        var p = await svc.CreateAsync(Req(scope: "SELECTED", products: new[] { a.PublicId }), default);

        var changed = await svc.UpdateAsync(p.PublicId, new TaxExemptionPeriodPatchRequest(Name: "Agosto", ValidFrom: Today.AddDays(11), ProductPublicIds: new[] { b.PublicId }), default);
        Assert.Equal(("Agosto", Today.AddDays(11)), (changed.Name, changed.ValidFrom));
        Assert.Equal(new[] { "B-1" }, changed.Products.Select(x => x.Sku));
        // vuelve A: se reactiva su fila (no se duplica) y B queda con IsActive = 0
        var back = await svc.UpdateAsync(p.PublicId, new TaxExemptionPeriodPatchRequest(ProductPublicIds: new[] { a.PublicId }), default);
        Assert.Equal(new[] { "A-1" }, back.Products.Select(x => x.Sku));
        var rows = await f.Db.Set<TaxExemptionPeriodProduct>().AsNoTracking().OrderBy(x => x.ProductId).ToListAsync();
        Assert.Equal(new[] { (a.ProductId, true), (b.ProductId, false) }, rows.Select(x => (x.ProductId, x.IsActive)).ToArray());
        // a ALL: la selección se da de baja
        var all = await svc.UpdateAsync(p.PublicId, new TaxExemptionPeriodPatchRequest(Scope: "ALL"), default);
        Assert.Equal(("ALL", 0), (all.Scope, all.Products.Count));
        Assert.All(await f.Db.Set<TaxExemptionPeriodProduct>().AsNoTracking().ToListAsync(), x => Assert.False(x.IsActive));

        // concurrencia: un rowVersion que no es el actual → 409
        await Assert.ThrowsAsync<ConflictException>(() => svc.UpdateAsync(p.PublicId, new TaxExemptionPeriodPatchRequest(Name: "Otro", RowVersion: Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })), default));
    }

    [Fact]
    public async Task Once_started_only_the_end_date_moves_and_it_cannot_go_before_today()
    {
        var (f, svc) = await CreateAsync();
        await using var _ = f;
        var a = await f.AddProductAsync("A-1");
        var p = await svc.CreateAsync(Req(from: Today, to: Today.AddDays(10), scope: "SELECTED", products: new[] { a.PublicId }), default);
        Assert.Equal(TaxExemptionPeriodStates.Active, p.State);

        async Task<string> Conflict(TaxExemptionPeriodPatchRequest r) => (await Assert.ThrowsAsync<ConflictException>(() => svc.UpdateAsync(p.PublicId, r, default))).Message;
        Assert.Equal(TaxExemptionPeriodRules.StartedOnlyEnd, await Conflict(new TaxExemptionPeriodPatchRequest(Scope: "ALL")));
        Assert.Equal(TaxExemptionPeriodRules.StartedOnlyEnd, await Conflict(new TaxExemptionPeriodPatchRequest(Name: "Otro nombre")));
        Assert.Equal(TaxExemptionPeriodRules.StartedOnlyEnd, await Conflict(new TaxExemptionPeriodPatchRequest(ProductPublicIds: Array.Empty<Guid>(), CategoryIds: new[] { 9101 })));
        Assert.Equal(TaxExemptionPeriodRules.EndBeforeToday, await Bad(() => svc.UpdateAsync(p.PublicId, new TaxExemptionPeriodPatchRequest(ValidTo: Today.AddDays(-1)), default)));

        // el formulario completo con lo mismo de siempre y otra fecha final sí pasa
        var moved = await svc.UpdateAsync(p.PublicId, new TaxExemptionPeriodPatchRequest(p.Name, p.Reason, p.ValidFrom, Today.AddDays(30), p.Scope, new[] { a.PublicId }), default);
        Assert.Equal(Today.AddDays(30), moved.ValidTo);
        var shortened = await svc.UpdateAsync(p.PublicId, new TaxExemptionPeriodPatchRequest(ValidTo: Today), default);
        Assert.Equal(Today, shortened.ValidTo);
    }

    [Fact]
    public async Task Deactivate_and_reactivate()
    {
        var (f, svc) = await CreateAsync();
        await using var _ = f;
        var p = await svc.CreateAsync(Req(), default);
        var off = await svc.SetActiveAsync(p.PublicId, false, default);
        Assert.Equal((false, TaxExemptionPeriodStates.Inactive), (off.IsActive, off.State));
        Assert.Empty(await svc.ListAsync(false, default));
        Assert.Single(await svc.ListAsync(true, default));
        var on = await svc.SetActiveAsync(p.PublicId, true, default);
        Assert.Equal((true, TaxExemptionPeriodStates.Scheduled), (on.IsActive, on.State));
        Assert.Single(await f.Db.Set<TaxExemptionPeriod>().AsNoTracking().ToListAsync());   // nunca DELETE
    }
}
