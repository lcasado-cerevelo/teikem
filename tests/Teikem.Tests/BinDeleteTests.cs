using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-12 — Eliminar posiciones (docs/posiciones-eliminar-diseno.md): «Eliminar» es definitivo (IsRemoved, inactiva, no se reactiva, no sale en ninguna lista, su código
/// queda libre y el historial la ve con «(eliminada)»); «Desactivar» sigue siendo temporal. Se rechaza con 409 y el motivo exacto con inventario, reservado, tareas abiertas,
/// posición de recepción por defecto, preferida de un producto o conteo abierto. Varias a la vez es PARCIAL.
/// </summary>
public class BinDeleteTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<WarehouseLayoutService>();
        s.AddSingleton(sp => new InventoryReadService(sp.GetRequiredService<Teikem.Infrastructure.Persistence.TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(),
            sp.GetRequiredService<ILookupCache>(), TenantClock.Default));
    });

    private sealed record World(WmsFixture F, Warehouse W, WarehouseZone Z, WarehouseBin A, WarehouseBin B);

    private static async Task<World> WorldAsync()
    {
        var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var z = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        return new World(f, w, z, await f.AddBinAsync(z, "A-01"), await f.AddBinAsync(z, "A-02"));
    }

    private static async Task<WarehouseBin> ReloadAsync(WmsFixture f, int id)
    {
        f.Db.ChangeTracker.Clear();
        return await f.Db.WarehouseBins.AsNoTracking().SingleAsync(b => b.WarehouseBinId == id);
    }

    [Fact]
    public async Task An_empty_active_bin_is_removed_for_good()
    {
        var x = await WorldAsync();
        var layout = x.F.Get<WarehouseLayoutService>();
        await layout.DeleteBinAsync(x.W.PublicId, x.A.WarehouseBinId, default);

        var saved = await ReloadAsync(x.F, x.A.WarehouseBinId);
        Assert.True(saved.IsRemoved);
        Assert.False(saved.IsActive);   // una eliminada siempre está inactiva
        Assert.NotNull(saved.RemovedAtUtc);
        // no vuelve: reactivar, editar y confirmar → 409
        var react = await Assert.ThrowsAsync<ConflictException>(() => layout.SetBinActiveAsync(x.W.PublicId, x.A.WarehouseBinId, true, default));
        Assert.Equal(WarehouseRules.BinDeletedMessage, react.Message);
        await Assert.ThrowsAsync<ConflictException>(() => layout.UpdateBinAsync(x.W.PublicId, x.A.WarehouseBinId, new WarehouseBinPatchRequest(Aisle: "9"), default));
        // eliminarla otra vez también es 409
        var again = await Assert.ThrowsAsync<ConflictException>(() => layout.DeleteBinAsync(x.W.PublicId, x.A.WarehouseBinId, default));
        Assert.Equal(WarehouseRules.BinAlreadyDeleted, again.Message);
    }

    [Fact]
    public async Task A_removed_bin_is_in_no_list_not_even_with_include_inactive_and_not_in_the_search()
    {
        var x = await WorldAsync();
        var layout = x.F.Get<WarehouseLayoutService>();
        await layout.DeleteBinAsync(x.W.PublicId, x.A.WarehouseBinId, default);
        await layout.SetBinActiveAsync(x.W.PublicId, x.B.WarehouseBinId, false, default);   // la desactivada sí sale con «incluir inactivas»

        var normal = await layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(), default);
        Assert.Empty(normal.Items);
        var inactive = await layout.ListBinsAsync(x.W.PublicId, new WarehouseBinQuery(IncludeInactive: true), default);
        Assert.Equal(new[] { "A-02" }, inactive.Items.Select(b => b.Code).ToArray());

        var found = await layout.SearchBinsAsync("A-0", null, includeInactive: true, 50, default);
        Assert.Equal(new[] { "A-02" }, found.Select(b => b.Code).ToArray());
    }

    [Fact]
    public async Task The_code_of_a_removed_bin_is_free_but_the_code_of_an_inactive_one_is_not()
    {
        var x = await WorldAsync();
        var layout = x.F.Get<WarehouseLayoutService>();
        await layout.DeleteBinAsync(x.W.PublicId, x.A.WarehouseBinId, default);
        var created = await layout.CreateBinAsync(x.W.PublicId, new WarehouseBinRequest(ZoneId: x.Z.WarehouseZoneId, Code: "A-01"), default);
        Assert.Equal("A-01", created.Code);
        Assert.NotEqual(x.A.WarehouseBinId, created.Id);   // es otra posición, con su propio historial

        await layout.SetBinActiveAsync(x.W.PublicId, x.B.WarehouseBinId, false, default);
        var inactive = await Assert.ThrowsAsync<ConflictException>(() => layout.CreateBinAsync(x.W.PublicId, new WarehouseBinRequest(ZoneId: x.Z.WarehouseZoneId, Code: "A-02"), default));
        Assert.Equal(WarehouseRules.BinInactiveCodeExists, inactive.Message);
        var active = await Assert.ThrowsAsync<ConflictException>(() => layout.CreateBinAsync(x.W.PublicId, new WarehouseBinRequest(ZoneId: x.Z.WarehouseZoneId, Code: "A-01"), default));
        Assert.Equal(WarehouseRules.DuplicateBinMessage, active.Message);
    }

    [Fact]
    public async Task Stock_reserved_tasks_defaults_and_preferred_block_the_removal_with_the_exact_reason()
    {
        var x = await WorldAsync();
        var layout = x.F.Get<WarehouseLayoutService>();
        var p = await x.F.AddProductAsync("PN");
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 3m, ToWarehouseId: x.W.WarehouseId, ToBinId: x.A.WarehouseBinId));

        var stock = await Assert.ThrowsAsync<ConflictException>(() => layout.DeleteBinAsync(x.W.PublicId, x.A.WarehouseBinId, default));
        Assert.Equal(WarehouseRules.BinDeleteHasStock("A-01"), stock.Message);
        Assert.False((await ReloadAsync(x.F, x.A.WarehouseBinId)).IsRemoved);

        // tarea abierta
        await x.F.AddTaskAsync(new WarehouseTaskSpec(WarehouseTaskTypes.Putaway, x.W.WarehouseId, p.ProductId, 1m, FromBinId: x.A.WarehouseBinId, ToBinId: x.B.WarehouseBinId));
        var task = await Assert.ThrowsAsync<ConflictException>(() => layout.DeleteBinAsync(x.W.PublicId, x.B.WarehouseBinId, default));
        Assert.Equal(WarehouseRules.BinDeleteHasOpenTasks("A-02"), task.Message);

        // posición de recepción por defecto del almacén
        var c = await x.F.AddBinAsync(x.Z, "C-01");
        var wh = await x.F.Db.Warehouses.SingleAsync(w => w.WarehouseId == x.W.WarehouseId);
        wh.DefaultReceivingBinId = c.WarehouseBinId;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var def = await Assert.ThrowsAsync<ConflictException>(() => layout.DeleteBinAsync(x.W.PublicId, c.WarehouseBinId, default));
        Assert.Equal(WarehouseRules.BinIsDefaultReceiving("C-01", "W1"), def.Message);

        // posición preferida de un producto
        var d = await x.F.AddBinAsync(x.Z, "D-01");
        await x.F.AddProductAsync("PREF", preferredWarehouseId: x.W.WarehouseId, preferredBinId: d.WarehouseBinId);
        var pref = await Assert.ThrowsAsync<ConflictException>(() => layout.DeleteBinAsync(x.W.PublicId, d.WarehouseBinId, default));
        Assert.Equal(WarehouseRules.BinIsPreferredOf("D-01", "PREF"), pref.Message);
    }

    [Fact]
    public async Task A_bin_with_history_can_be_removed_and_the_kardex_shows_it_as_removed()
    {
        var x = await WorldAsync();
        var layout = x.F.Get<WarehouseLayoutService>();
        var p = await x.F.AddProductAsync("PN");
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 3m, ToWarehouseId: x.W.WarehouseId, ToBinId: x.A.WarehouseBinId));
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Issue, p.ProductId, 3m, FromWarehouseId: x.W.WarehouseId, FromBinId: x.A.WarehouseBinId));   // queda en cero, con historial

        await layout.DeleteBinAsync(x.W.PublicId, x.A.WarehouseBinId, default);
        x.F.Db.ChangeTracker.Clear();

        var kardex = await x.F.Get<InventoryReadService>().KardexAsync(new KardexQuery(), InventoryScope.Any, default);
        Assert.Equal(2, kardex.Total);   // el historial no se pierde
        Assert.Contains(kardex.Items, r => r.ToBinCode == "A-01 (eliminada)");
        Assert.Contains(kardex.Items, r => r.FromBinCode == "A-01 (eliminada)");
    }

    [Fact]
    public async Task Deleting_several_is_partial_with_a_result_per_bin()
    {
        var x = await WorldAsync();
        var layout = x.F.Get<WarehouseLayoutService>();
        var c = await x.F.AddBinAsync(x.Z, "A-03");
        var p = await x.F.AddProductAsync("PN");
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 2m, ToWarehouseId: x.W.WarehouseId, ToBinId: x.B.WarehouseBinId));   // A-02 con inventario
        var other = await x.F.AddWarehouseAsync("W2");
        var foreign = await x.F.AddBinAsync(await x.F.AddZoneAsync(other, "PCK", ZoneTypes.Picking), "X-01");

        var result = await layout.DeleteBinsAsync(x.W.PublicId,
            new WarehouseBinsDeleteRequest(new[] { x.A.WarehouseBinId, x.B.WarehouseBinId, c.WarehouseBinId, foreign.WarehouseBinId, x.A.WarehouseBinId }), default);

        Assert.Equal(new[] { "A-01", "A-03" }, result.Deleted.Select(d => d.Code).OrderBy(s => s).ToArray());
        Assert.Equal(2, result.Skipped.Count);
        Assert.Equal(WarehouseRules.BinDeleteHasStock("A-02"), result.Skipped.Single(s => s.BinId == x.B.WarehouseBinId).Reason);
        Assert.Equal(WarehouseRules.BinNotFoundToDelete, result.Skipped.Single(s => s.BinId == foreign.WarehouseBinId).Reason);   // de otro almacén: no existe aquí
        Assert.True((await ReloadAsync(x.F, x.A.WarehouseBinId)).IsRemoved);
        Assert.False((await ReloadAsync(x.F, x.B.WarehouseBinId)).IsRemoved);
        Assert.False((await ReloadAsync(x.F, foreign.WarehouseBinId)).IsRemoved);

        var none = await Assert.ThrowsAsync<ValidationException>(() => layout.DeleteBinsAsync(x.W.PublicId, new WarehouseBinsDeleteRequest(Array.Empty<int>()), default));
        Assert.Contains("binIds", none.Errors!.Keys);
    }

    [Fact]
    public async Task Deactivate_is_still_temporary_and_reversible()
    {
        var x = await WorldAsync();
        var layout = x.F.Get<WarehouseLayoutService>();
        await layout.SetBinActiveAsync(x.W.PublicId, x.A.WarehouseBinId, false, default);
        var back = await layout.SetBinActiveAsync(x.W.PublicId, x.A.WarehouseBinId, true, default);
        Assert.True(back.IsActive);
        Assert.False((await ReloadAsync(x.F, x.A.WarehouseBinId)).IsRemoved);
        // y una desactivada también se puede eliminar (vacía)
        await layout.SetBinActiveAsync(x.W.PublicId, x.A.WarehouseBinId, false, default);
        await layout.DeleteBinAsync(x.W.PublicId, x.A.WarehouseBinId, default);
        Assert.True((await ReloadAsync(x.F, x.A.WarehouseBinId)).IsRemoved);
    }

    [Fact]
    public void The_update_sql_adds_the_columns_the_check_and_the_filtered_unique_indexes()
    {
        var sql = File.ReadAllText(Path.Combine(RepoRoot(), "Diseño", "logistica-db-update.sql"));
        var i = sql.IndexOf("Eliminar posiciones (docs/posiciones-eliminar-diseno.md)", StringComparison.Ordinal);
        Assert.True(i > 0);
        var section = sql[i..].Replace("\r\n", "\n");
        Assert.Contains("IF COL_LENGTH('dbo.WarehouseBin', 'IsRemoved') IS NULL", section);
        Assert.Contains("CK_WarehouseBin_Removed CHECK (IsRemoved = 0 OR IsActive = 0)", section);
        Assert.Contains("CREATE UNIQUE INDEX UQ_WarehouseBin ON dbo.WarehouseBin (WarehouseZoneId, Code) WHERE IsRemoved = 0", section);
        Assert.Contains("CREATE UNIQUE INDEX UQ_WarehouseBin_WhCode ON dbo.WarehouseBin (WarehouseId, Code) WHERE IsRemoved = 0", section);
        Assert.Contains("DROP CONSTRAINT UQ_WarehouseBin;", section);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Teikem.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
