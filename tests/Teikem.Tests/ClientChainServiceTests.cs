using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Interceptors;
using Teikem.Infrastructure.Services;
using Xunit;
using R = Teikem.Domain.Clients.ClientChainRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 245 (Lote B del plan de rutas, cadenas y precios) — <see cref="ClientChainService"/> en la base en memoria con dos compañías (datos INVENTADOS): alta, lista y
/// unicidad del nombre, guardas de activar y desactivar, asignar / quitar / mover, la cadena desde la ficha del cliente, aislamiento entre compañías, bitácora
/// (con el interceptor real y el EntityType CLIENT_CHAIN sembrado como en la sección SQL) y el alta masiva por el inicio del nombre (simulación sin escritura,
/// huella, exclusiones, tope de 2000 e idempotencia). La concurrencia real (RowVersion, índice filtrado, FK compuesta) la prueba ClientChainSqlTests.
/// </summary>
public sealed class ClientChainServiceTests
{
    private const int Other = WmsFixture.OtherTenantId;
    private static readonly DateTime Now = new(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);

    private static ClientChainService Svc(WmsFixture f) => new(f.Db, f.Tenant, new TenantClock(TenantClock.Default.Zone, () => Now));

    private static int _code;

    /// <summary>Un cliente con el nombre pedido (código único), activo o no, de la compañía activa o de otra.</summary>
    private static async Task<Client> ClientAsync(WmsFixture f, string name, bool isActive = true, int? tenantId = null, int? chainId = null)
    {
        var c = new Client
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId ?? f.Tenant.TenantId!.Value, Code = "CC" + Interlocked.Increment(ref _code), Name = name,
            StatusCodeId = f.StatusId(StatusDomains.ClientStatus, ClientStatuses.Active), IsActive = isActive, CreatedAtUtc = DateTime.UtcNow, ChainId = chainId,
        };
        f.Db.Clients.Add(c);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return c;
    }

    private static async Task<int?> ChainIdOfAsync(WmsFixture f, Guid clientPublicId)
        => await f.Db.Clients.IgnoreQueryFilters().AsNoTracking().Where(c => c.PublicId == clientPublicId).Select(c => c.ChainId).SingleAsync();

    private static async Task<int> InternalIdAsync(WmsFixture f, Guid chainPublicId)
        => await f.Db.Set<ClientChain>().IgnoreQueryFilters().AsNoTracking().Where(c => c.PublicId == chainPublicId).Select(c => c.ClientChainId).SingleAsync();

    private static ClientChainAssignRequest Assign(params Client[] clients) => new(clients.Select(c => c.PublicId).ToList());

    // ================================================================ alta, lista y nombre

    [Fact]
    public async Task Create_list_and_unique_name()
    {
        await using var f = await WmsFixture.CreateAsync();
        var svc = Svc(f);

        var walg = await svc.CreateAsync(new ClientChainRequest("  Farmacias Caño  ", " notas "), default);
        Assert.Equal("Farmacias Caño", walg.Name);
        Assert.Equal("notas", walg.Notes);
        Assert.True(walg.IsActive);
        Assert.Equal(0, walg.ClientCount);
        Assert.Equal(Now, walg.CreatedAtUtc);

        // mismo nombre con otras mayúsculas, sin acento y con punto → 409
        var dup = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateAsync(new ClientChainRequest("FARMACIAS. CANO", null), default));
        Assert.Equal(R.NameTaken, dup.Message);
        var empty = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ClientChainRequest("   ", null), default));
        Assert.Equal(R.NameRequired, empty.Message);
        var longName = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ClientChainRequest(new string('x', 101), null), default));
        Assert.Equal(R.NameTooLong, longName.Message);
        var longNotes = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ClientChainRequest("CVS", new string('n', 501)), default));
        Assert.Equal(R.NotesTooLong, longNotes.Message);
        var noBody = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(null!, default));
        Assert.Equal(R.BodyRequired, noBody.Message);

        // ClientCount = clientes ACTIVOS; la ficha cuenta además los inactivos
        var a1 = await ClientAsync(f, "Caño 1");
        var a2 = await ClientAsync(f, "Caño 2");
        var off = await ClientAsync(f, "Caño viejo", isActive: false);
        await svc.AssignAsync(walg.PublicId, Assign(a1, a2, off), default);
        var cvs = await svc.CreateAsync(new ClientChainRequest("CVS", null), default);
        var list = await svc.ListAsync(false, null, default);
        Assert.Equal(new[] { "CVS", "Farmacias Caño" }, list.Select(x => x.Name));
        Assert.Equal(2, list.Single(x => x.PublicId == walg.PublicId).ClientCount);
        Assert.Equal(0, list.Single(x => x.PublicId == cvs.PublicId).ClientCount);
        Assert.Single(await svc.ListAsync(false, "CV", default));
        var detail = await svc.GetAsync(walg.PublicId, default);
        Assert.Equal((2, 1), (detail.ClientCount, detail.InactiveClientCount));

        // una inactiva no estorba al nombre; reactivarla con el nombre tomado → 409
        await svc.SetActiveAsync(cvs.PublicId, false, default);
        Assert.DoesNotContain(await svc.ListAsync(false, null, default), x => x.PublicId == cvs.PublicId);
        Assert.Contains(await svc.ListAsync(true, null, default), x => x.PublicId == cvs.PublicId);
        var cvs2 = await svc.CreateAsync(new ClientChainRequest("cvs", null), default);
        var reactivate = await Assert.ThrowsAsync<ConflictException>(() => svc.SetActiveAsync(cvs.PublicId, true, default));
        Assert.Equal(R.NameTaken, reactivate.Message);

        // renombrar: a un nombre tomado → 409; ClearNotes borra las notas; null no cambia
        var rename = await Assert.ThrowsAsync<ConflictException>(() => svc.UpdateAsync(cvs2.PublicId, new ClientChainRequest("farmacias caño", null), default));
        Assert.Equal(R.NameTaken, rename.Message);
        var edited = await svc.UpdateAsync(walg.PublicId, new ClientChainRequest(null, null, ClearNotes: true), default);
        Assert.Equal(("Farmacias Caño", (string?)null), (edited.Name, edited.Notes));
        var renamed = await svc.UpdateAsync(walg.PublicId, new ClientChainRequest("Farmacias CAÑO", "otra"), default);   // la misma clave: no choca consigo misma
        Assert.Equal(("Farmacias CAÑO", "otra"), (renamed.Name, renamed.Notes));
    }

    // ================================================================ activar y desactivar

    [Fact]
    public async Task Deactivate_guard()
    {
        await using var f = await WmsFixture.CreateAsync();
        var svc = Svc(f);
        var chain = await svc.CreateAsync(new ClientChainRequest("Walgreens", null), default);
        var c1 = await ClientAsync(f, "Walgreens 1");
        var c2 = await ClientAsync(f, "Walgreens 2");
        var gone = await ClientAsync(f, "Walgreens cerrado", isActive: false);
        await svc.AssignAsync(chain.PublicId, Assign(c1, c2, gone), default);

        var plural = await Assert.ThrowsAsync<ConflictException>(() => svc.SetActiveAsync(chain.PublicId, false, default));
        Assert.Equal("La cadena tiene 2 clientes activos: quítelos de la cadena antes de desactivarla.", plural.Message);
        await svc.RemoveAsync(chain.PublicId, new ClientChainRemoveRequest(new[] { c2.PublicId }), default);
        var singular = await Assert.ThrowsAsync<ConflictException>(() => svc.SetActiveAsync(chain.PublicId, false, default));
        Assert.Equal("La cadena tiene 1 cliente activo: quítelo de la cadena antes de desactivarla.", singular.Message);
        await svc.RemoveAsync(chain.PublicId, new ClientChainRemoveRequest(new[] { c1.PublicId }), default);

        // con solo clientes inactivos se desactiva (el inactivo conserva su cadena); repetir no cambia nada
        var off = await svc.SetActiveAsync(chain.PublicId, false, default);
        Assert.False(off.IsActive);
        Assert.Equal(1, off.InactiveClientCount);
        Assert.False((await svc.SetActiveAsync(chain.PublicId, false, default)).IsActive);
        Assert.NotNull(await ChainIdOfAsync(f, gone.PublicId));

        // asignar a una cadena inactiva → 400; quitar de ella sí se puede
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => svc.AssignAsync(chain.PublicId, Assign(c1), default));
        Assert.Equal(R.ChainInactive, inactive.Message);
        var removed = await svc.RemoveAsync(chain.PublicId, new ClientChainRemoveRequest(new[] { gone.PublicId }), default);
        Assert.Equal(1, removed.Removed);
        Assert.True((await svc.SetActiveAsync(chain.PublicId, true, default)).IsActive);
    }

    // ================================================================ asignar, quitar y mover

    [Fact]
    public async Task Assign_remove_and_move()
    {
        await using var f = await WmsFixture.CreateAsync();
        var svc = Svc(f);
        var walg = await svc.CreateAsync(new ClientChainRequest("Walgreens", null), default);
        var cvs = await svc.CreateAsync(new ClientChainRequest("CVS", null), default);
        var w1 = await ClientAsync(f, "Walgreens 1");
        var w2 = await ClientAsync(f, "Walgreens 2");
        var inCvs = await ClientAsync(f, "Walgreens en CVS");
        await svc.AssignAsync(cvs.PublicId, Assign(inCvs), default);

        // el de otra cadena se salta y se reporta
        var r1 = await svc.AssignAsync(walg.PublicId, Assign(w1, w2, inCvs), default);
        Assert.Equal((2, 0, 0, 0, 0), (r1.Assigned, r1.Moved, r1.AlreadyInChain, r1.Removed, r1.NotInChain));
        var skipped = Assert.Single(r1.SkippedOtherChain);
        Assert.Equal((inCvs.PublicId, inCvs.Code, inCvs.Name, cvs.PublicId, "CVS"), (skipped.ClientPublicId, skipped.Code, skipped.Name, skipped.ChainPublicId, skipped.ChainName));
        Assert.Equal(await InternalIdAsync(f, cvs.PublicId), await ChainIdOfAsync(f, inCvs.PublicId));

        // con moveFromOtherChains se mueve; repetir cuenta ALREADY
        var r2 = await svc.AssignAsync(walg.PublicId, new ClientChainAssignRequest(new[] { w1.PublicId, inCvs.PublicId, w1.PublicId }, MoveFromOtherChains: true), default);
        Assert.Equal((0, 1, 1), (r2.Assigned, r2.Moved, r2.AlreadyInChain));
        Assert.Empty(r2.SkippedOtherChain);
        Assert.Equal(await InternalIdAsync(f, walg.PublicId), await ChainIdOfAsync(f, inCvs.PublicId));

        // quitar ignora a los que no están en la cadena
        var other = await ClientAsync(f, "Sin cadena");
        var r3 = await svc.RemoveAsync(walg.PublicId, new ClientChainRemoveRequest(new[] { w2.PublicId, other.PublicId }), default);
        Assert.Equal((1, 1), (r3.Removed, r3.NotInChain));
        Assert.Null(await ChainIdOfAsync(f, w2.PublicId));
        var again = await svc.RemoveAsync(walg.PublicId, new ClientChainRemoveRequest(new[] { w2.PublicId }), default);
        Assert.Equal((0, 1), (again.Removed, again.NotInChain));

        // validaciones de la lista
        var tooMany = await Assert.ThrowsAsync<ValidationException>(() =>
            svc.AssignAsync(walg.PublicId, new ClientChainAssignRequest(Enumerable.Range(0, R.MaxBulk + 1).Select(_ => Guid.NewGuid()).ToList()), default));
        Assert.Equal("No se pueden mandar más de 2000 clientes en una sola llamada.", tooMany.Message);
        var empty = await Assert.ThrowsAsync<ValidationException>(() => svc.AssignAsync(walg.PublicId, new ClientChainAssignRequest(Array.Empty<Guid>()), default));
        Assert.Equal("Indique los clientes (clientPublicIds).", empty.Message);
        var nullList = await Assert.ThrowsAsync<ValidationException>(() => svc.RemoveAsync(walg.PublicId, new ClientChainRemoveRequest(null), default));
        Assert.Equal(R.ClientsRequired, nullList.Message);
        var noBody = await Assert.ThrowsAsync<ValidationException>(() => svc.AssignAsync(walg.PublicId, null!, default));
        Assert.Equal("El cuerpo de la solicitud es obligatorio.", noBody.Message);
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => svc.AssignAsync(walg.PublicId, new ClientChainAssignRequest(new[] { w1.PublicId, Guid.NewGuid() }), default));
        Assert.Equal("Cliente no encontrado.", unknown.Message);
    }

    [Fact]
    public async Task Set_client_chain_from_the_client_moves_removes_and_checks_the_target()
    {
        await using var f = await WmsFixture.CreateAsync();
        var svc = Svc(f);
        var walg = await svc.CreateAsync(new ClientChainRequest("Walgreens", null), default);
        var cvs = await svc.CreateAsync(new ClientChainRequest("CVS", null), default);
        var off = await svc.CreateAsync(new ClientChainRequest("Cerrada", null), default);
        await svc.SetActiveAsync(off.PublicId, false, default);
        var c = await ClientAsync(f, "Farmacia X");

        var m1 = await svc.SetClientChainAsync(c.PublicId, new ClientChainSetRequest(walg.PublicId), default);
        Assert.Equal((c.PublicId, (Guid?)walg.PublicId, "Walgreens", true), (m1.ClientPublicId, m1.ChainPublicId, m1.ChainName, m1.ChainIsActive));
        // mover en un solo paso (cambio deliberado)
        var m2 = await svc.SetClientChainAsync(c.PublicId, new ClientChainSetRequest(cvs.PublicId), default);
        Assert.Equal(cvs.PublicId, m2.ChainPublicId);
        Assert.Equal(0, (await svc.GetAsync(walg.PublicId, default)).ClientCount);
        // a una inactiva → 400; a una que no existe → 404
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => svc.SetClientChainAsync(c.PublicId, new ClientChainSetRequest(off.PublicId), default));
        Assert.Equal(R.ChainInactive, inactive.Message);
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => svc.SetClientChainAsync(c.PublicId, new ClientChainSetRequest(Guid.NewGuid()), default));
        Assert.Equal(R.NotFound, missing.Message);
        var noClient = await Assert.ThrowsAsync<NotFoundException>(() => svc.SetClientChainAsync(Guid.NewGuid(), new ClientChainSetRequest(null), default));
        Assert.Equal("Cliente no encontrado.", noClient.Message);
        // quitar (null), dos veces
        var m3 = await svc.SetClientChainAsync(c.PublicId, new ClientChainSetRequest(null), default);
        Assert.Equal(((Guid?)null, (string?)null, false), (m3.ChainPublicId, m3.ChainName, m3.ChainIsActive));
        Assert.Null((await svc.SetClientChainAsync(c.PublicId, new ClientChainSetRequest(null), default)).ChainPublicId);
    }

    [Fact]
    public async Task List_clients_pages_searches_and_brings_everything_with_all()
    {
        await using var f = await WmsFixture.CreateAsync();
        var svc = Svc(f);
        var chain = await svc.CreateAsync(new ClientChainRequest("Walgreens", null), default);
        var clients = new List<Client>();
        for (var i = 1; i <= 7; i++) clients.Add(await ClientAsync(f, $"Walgreens {i}"));
        clients.Add(await ClientAsync(f, "Walgreens viejo", isActive: false));
        await svc.AssignAsync(chain.PublicId, Assign(clients.ToArray()), default);

        var page = await svc.ListClientsAsync(chain.PublicId, null, false, 2, 3, false, default);
        Assert.Equal(7, page.Total);
        Assert.Equal(new[] { "Walgreens 3", "Walgreens 4", "Walgreens 5" }, page.Items.Select(x => x.Name));
        Assert.All(page.Items, x => Assert.Equal(ClientStatuses.Active, x.Status));
        Assert.Equal(8, (await svc.ListClientsAsync(chain.PublicId, null, true, 0, 50, false, default)).Total);
        var all = await svc.ListClientsAsync(chain.PublicId, null, true, 5, 1, true, default);
        Assert.Equal((8, 8, 0), (all.Items.Count, all.Take, all.Skip));
        Assert.Single((await svc.ListClientsAsync(chain.PublicId, "viejo", true, 0, 50, false, default)).Items);
        Assert.Equal(R.MaxPageSize, (await svc.ListClientsAsync(chain.PublicId, null, false, 0, 10_000, false, default)).Take);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.ListClientsAsync(Guid.NewGuid(), null, false, 0, 50, false, default));
    }

    // ================================================================ aislamiento entre compañías

    [Fact]
    public async Task Tenant_isolation()
    {
        await using var f = await WmsFixture.CreateAsync();
        var svc = Svc(f);
        var chainA = await svc.CreateAsync(new ClientChainRequest("Walgreens", null), default);
        var clientA = await ClientAsync(f, "Walgreens A");
        var clientB = await ClientAsync(f, "Walgreens B", tenantId: Other);

        // un PublicId de cliente de otra compañía → 404 «Cliente no encontrado.»
        var foreignClient = await Assert.ThrowsAsync<NotFoundException>(() => svc.AssignAsync(chainA.PublicId, Assign(clientB), default));
        Assert.Equal("Cliente no encontrado.", foreignClient.Message);
        await svc.AssignAsync(chainA.PublicId, Assign(clientA), default);

        using (f.AsTenant(Other))
        {
            var b = Svc(f);
            Assert.Empty(await b.ListAsync(true, null, default));
            var get = await Assert.ThrowsAsync<NotFoundException>(() => b.GetAsync(chainA.PublicId, default));
            Assert.Equal("Cadena no encontrada.", get.Message);
            await Assert.ThrowsAsync<NotFoundException>(() => b.ListClientsAsync(chainA.PublicId, null, true, 0, 50, false, default));
            await Assert.ThrowsAsync<NotFoundException>(() => b.AssignAsync(chainA.PublicId, Assign(clientB), default));
            await Assert.ThrowsAsync<NotFoundException>(() => b.RemoveAsync(chainA.PublicId, new ClientChainRemoveRequest(new[] { clientA.PublicId }), default));
            await Assert.ThrowsAsync<NotFoundException>(() => b.SetActiveAsync(chainA.PublicId, false, default));
            await Assert.ThrowsAsync<NotFoundException>(() => b.UpdateAsync(chainA.PublicId, new ClientChainRequest("x", null), default));
            await Assert.ThrowsAsync<NotFoundException>(() => b.PreviewBulkAsync(chainA.PublicId, new ClientChainBulkRequest("Walgreens"), default));
            var byClient = await Assert.ThrowsAsync<NotFoundException>(() => b.SetClientChainAsync(clientB.PublicId, new ClientChainSetRequest(chainA.PublicId), default));
            Assert.Equal("Cadena no encontrada.", byClient.Message);
            // el cliente de A no existe para B
            await Assert.ThrowsAsync<NotFoundException>(() => b.SetClientChainAsync(clientA.PublicId, new ClientChainSetRequest(null), default));
            // B puede tener su propia cadena con el mismo nombre
            var chainB = await b.CreateAsync(new ClientChainRequest("Walgreens", null), default);
            var preview = await b.PreviewBulkAsync(chainB.PublicId, new ClientChainBulkRequest("walgreens"), default);
            Assert.Equal(clientB.PublicId, Assert.Single(preview.Rows).ClientPublicId);
        }
        Assert.Null(await ChainIdOfAsync(f, clientB.PublicId));
        Assert.NotNull(await ChainIdOfAsync(f, clientA.PublicId));
    }

    // ================================================================ bitácora

    [Fact]
    public async Task Audit_log()
    {
        var tenant = new TenantContext { TenantId = 1, UserId = 7, IsAuthenticated = true };
        var lookups = new TripTestLookups();
        lookups.Load(new[]
        {
            new LookupCode { LookupCodeId = 1, Entity = LookupDomains.EntityType, InternalCode = ClientChainCodes.EntityType, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 2, Entity = LookupDomains.EntityType, InternalCode = EntityTypes.Client, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 3, Entity = LookupDomains.AuditAction, InternalCode = AuditActions.Create, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 4, Entity = LookupDomains.AuditAction, InternalCode = AuditActions.Update, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 5, Entity = LookupDomains.AuditAction, InternalCode = AuditActions.Delete, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = 6, Entity = LookupDomains.AuditAction, InternalCode = AuditActions.Restore, LabelJson = "{}", IsActive = true },
        });
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("chain-audit-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .AddInterceptors(new AuditSaveChangesInterceptor(tenant, lookups, NullLogger<AuditSaveChangesInterceptor>.Instance))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new TeikemDbContext(options, tenant);
        var client = new Client { ClientId = 50, PublicId = Guid.NewGuid(), TenantId = 1, Code = "AUD1", Name = "Walgreens Auditado", StatusCodeId = 1, IsActive = true, CreatedAtUtc = Now };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var svc = new ClientChainService(db, tenant, new TenantClock(TenantClock.Default.Zone, () => Now));

        Task<List<Teikem.Domain.Audit.AuditLog>> LogsAsync(int entityTypeId)
            => db.AuditLogs.IgnoreQueryFilters().AsNoTracking().Where(l => l.EntityTypeLookupId == entityTypeId).OrderBy(l => l.AuditLogId).ToListAsync();

        var chain = await svc.CreateAsync(new ClientChainRequest("Walgreens", null), default);
        var created = Assert.Single(await LogsAsync(1));
        Assert.Equal(3, created.ActionLookupId);
        Assert.Contains("Walgreens", created.ChangesJson);

        await svc.AssignAsync(chain.PublicId, new ClientChainAssignRequest(new[] { client.PublicId }), default);
        var clientLog = Assert.Single(await LogsAsync(2), l => l.ActionLookupId == 4);   // (el alta del cliente de la prueba también dejó su CREATE)
        Assert.Equal((4, 50), (clientLog.ActionLookupId, clientLog.EntityId));
        Assert.Contains("ChainId", clientLog.ChangesJson);
        // el «toque» de la cadena no deja bitácora (UpdatedAtUtc y UpdatedBy son [NotAudited])
        Assert.Single(await LogsAsync(1));
        Assert.NotNull((await db.Set<ClientChain>().AsNoTracking().SingleAsync()).UpdatedAtUtc);

        // editar sí deja bitácora de la cadena
        await svc.UpdateAsync(chain.PublicId, new ClientChainRequest(null, "nota"), default);
        Assert.Equal(2, (await LogsAsync(1)).Count);
    }

    // ================================================================ alta masiva

    private static async Task<(ClientChainDetailDto Walg, ClientChainDetailDto Cvs, Client W1, Client W2, Client W3, Client Boro, Client InCvs, Client Off)> BulkWorldAsync(
        WmsFixture f, ClientChainService svc)
    {
        var walg = await svc.CreateAsync(new ClientChainRequest("Walgreens", null), default);
        var cvs = await svc.CreateAsync(new ClientChainRequest("CVS", null), default);
        var w1 = await ClientAsync(f, "Walgreens 1");
        var w2 = await ClientAsync(f, "walgreens. #2");
        var w3 = await ClientAsync(f, "Walgreens-3");
        var boro = await ClientAsync(f, "Walgreensboro");
        var inCvs = await ClientAsync(f, "Walgreens 4");
        var off = await ClientAsync(f, "Walgreens cerrado", isActive: false);
        await ClientAsync(f, "Walgreens otra compañía", tenantId: Other);
        await svc.AssignAsync(cvs.PublicId, Assign(inCvs), default);
        return (walg, cvs, w1, w2, w3, boro, inCvs, off);
    }

    [Fact]
    public async Task Bulk_preview_writes_nothing()
    {
        await using var f = await WmsFixture.CreateAsync();
        var svc = Svc(f);
        var w = await BulkWorldAsync(f, svc);

        var p = await svc.PreviewBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("  WALGREENS "), default);
        Assert.Equal("WALGREENS", p.Prefix);
        Assert.Equal((4, 3, 0, 0, 1, 0, false, 2000), (p.Matched, p.ToAssign, p.ToMove, p.AlreadyInChain, p.SkippedOtherChain, p.Excluded, p.TooMany, p.MaxBulk));
        Assert.DoesNotContain(p.Rows, r => r.ClientPublicId == w.Boro.PublicId || r.ClientPublicId == w.Off.PublicId);
        var skip = Assert.Single(p.Rows, r => r.Action == R.ActionSkipOtherChain);
        Assert.Equal((w.InCvs.PublicId, (Guid?)w.Cvs.PublicId, "CVS"), (skip.ClientPublicId, skip.CurrentChainPublicId, skip.CurrentChainName));
        Assert.Equal(64, p.Fingerprint.Length);

        // con la exclusión la fila sale EXCLUDED y la huella es la misma; sin palabra completa entra Walgreensboro
        var excluded = await svc.PreviewBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", ExcludeClientPublicIds: new[] { w.W2.PublicId }), default);
        Assert.Equal(R.ActionExcluded, excluded.Rows.Single(r => r.ClientPublicId == w.W2.PublicId).Action);
        Assert.Equal((2, 1), (excluded.ToAssign, excluded.Excluded));
        Assert.Equal(p.Fingerprint, excluded.Fingerprint);
        var loose = await svc.PreviewBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", WholeWord: false, MoveFromOtherChains: true), default);
        Assert.Equal((5, 4, 1), (loose.Matched, loose.ToAssign, loose.ToMove));
        Assert.NotEqual(p.Fingerprint, loose.Fingerprint);

        // la simulación no escribió nada
        foreach (var c in new[] { w.W1, w.W2, w.W3, w.Boro }) Assert.Null(await ChainIdOfAsync(f, c.PublicId));
        Assert.Equal(0, (await svc.GetAsync(w.Walg.PublicId, default)).ClientCount);

        // validaciones
        var shortPrefix = await Assert.ThrowsAsync<ValidationException>(() => svc.PreviewBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest(" a. "), default));
        Assert.Equal(R.PrefixTooShort, shortPrefix.Message);
        var outside = await Assert.ThrowsAsync<ValidationException>(() =>
            svc.PreviewBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", ExcludeClientPublicIds: new[] { w.Boro.PublicId }), default));
        Assert.Equal(R.ExclusionOutside, outside.Message);
        var cerrada = await svc.CreateAsync(new ClientChainRequest("Cerrada", null), default);
        await svc.SetActiveAsync(cerrada.PublicId, false, default);
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => svc.PreviewBulkAsync(cerrada.PublicId, new ClientChainBulkRequest("walgreens"), default));
        Assert.Equal(R.ChainInactive, inactive.Message);
    }

    [Fact]
    public async Task Bulk_preview_with_2500_clients_marks_too_many_and_is_fast()
    {
        await using var f = await WmsFixture.CreateAsync();
        var svc = Svc(f);
        var chain = await svc.CreateAsync(new ClientChainRequest("Walgreens", null), default);
        var statusId = f.StatusId(StatusDomains.ClientStatus, ClientStatuses.Active);
        for (var i = 0; i < 2500; i++)
            f.Db.Clients.Add(new Client
            {
                PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "V" + i, Name = i <= 2000 ? $"Walgreens #{i}" : $"Farmacia {i}",
                StatusCodeId = statusId, IsActive = true, CreatedAtUtc = Now,
            });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await svc.PreviewBulkAsync(chain.PublicId, new ClientChainBulkRequest("Farmacia"), default);   // calienta EF

        var sw = Stopwatch.StartNew();
        var p = await svc.PreviewBulkAsync(chain.PublicId, new ClientChainBulkRequest("walgreens"), default);
        sw.Stop();
        Assert.Equal(2001, p.Matched);
        Assert.True(p.TooMany);
        Assert.Equal(R.MaxBulk, p.Rows.Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"La simulación tardó {sw.Elapsed}.");

        var tooMany = await Assert.ThrowsAsync<ValidationException>(() =>
            svc.ApplyBulkAsync(chain.PublicId, new ClientChainBulkRequest("walgreens", Fingerprint: p.Fingerprint), default));
        Assert.Equal("La simulación encontró 2001 clientes y el máximo por aplicación es 2000: use un prefijo más largo.", tooMany.Message);
        Assert.Equal(0, await f.Db.Clients.CountAsync(c => c.ChainId != null));
    }

    [Fact]
    public async Task Bulk_apply_guards()
    {
        await using var f = await WmsFixture.CreateAsync();
        var svc = Svc(f);
        var w = await BulkWorldAsync(f, svc);
        var p = await svc.PreviewBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens"), default);

        var noPrint = await Assert.ThrowsAsync<ValidationException>(() => svc.ApplyBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens"), default));
        Assert.Equal("Falta la huella de la simulación (fingerprint): simule antes de aplicar.", noPrint.Message);
        var fake = await Assert.ThrowsAsync<ConflictException>(() => svc.ApplyBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", Fingerprint: "abc"), default));
        Assert.Equal(R.FingerprintStale, fake.Message);
        // otras opciones con la misma huella → 409 (la huella incluye palabra completa y mover)
        await Assert.ThrowsAsync<ConflictException>(() => svc.ApplyBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", MoveFromOtherChains: true, Fingerprint: p.Fingerprint), default));
        // exclusión ajena → 400
        var outside = await Assert.ThrowsAsync<ValidationException>(() =>
            svc.ApplyBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", ExcludeClientPublicIds: new[] { w.Boro.PublicId }, Fingerprint: p.Fingerprint), default));
        Assert.Equal(R.ExclusionOutside, outside.Message);

        // renombrar un cliente después de simular → 409 sin escribir nada
        var renamed = await f.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == w.W3.ClientId);
        renamed.Name = "Farmacia Tres";
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var stale = await Assert.ThrowsAsync<ConflictException>(() => svc.ApplyBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", Fingerprint: p.Fingerprint), default));
        Assert.Equal("Los clientes que coinciden cambiaron desde la simulación: vuelva a simular antes de aplicar.", stale.Message);
        Assert.Equal(0, (await svc.GetAsync(w.Walg.PublicId, default)).ClientCount);

        // asignar uno a otra cadena después de simular → 409
        var p2 = await svc.PreviewBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens"), default);
        await svc.AssignAsync(w.Cvs.PublicId, Assign(w.W1), default);
        await Assert.ThrowsAsync<ConflictException>(() => svc.ApplyBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", Fingerprint: p2.Fingerprint), default));
        Assert.Equal(0, (await svc.GetAsync(w.Walg.PublicId, default)).ClientCount);

        // aplicar bien: asigna, salta y excluye según los conteos
        var p3 = await svc.PreviewBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens"), default);
        Assert.Equal((3, 1, 2), (p3.Matched, p3.ToAssign, p3.SkippedOtherChain));   // W2 para asignar; W1 e InCvs en CVS
        var w5 = await ClientAsync(f, "Walgreens 5");
        await Assert.ThrowsAsync<ConflictException>(() => svc.ApplyBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", Fingerprint: p3.Fingerprint), default));   // entró uno
        var p4 = await svc.PreviewBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", ExcludeClientPublicIds: new[] { w5.PublicId }), default);
        var result = await svc.ApplyBulkAsync(w.Walg.PublicId,
            new ClientChainBulkRequest("walgreens", ExcludeClientPublicIds: new[] { w5.PublicId }, Fingerprint: p4.Fingerprint.ToUpperInvariant()), default);
        Assert.Equal((1, 0, 0, 2, 1), (result.Assigned, result.Moved, result.AlreadyInChain, result.SkippedOtherChain, result.Excluded));
        var walgId = await InternalIdAsync(f, w.Walg.PublicId);
        Assert.Equal(walgId, await ChainIdOfAsync(f, w.W2.PublicId));
        Assert.Null(await ChainIdOfAsync(f, w5.PublicId));
        Assert.NotEqual(walgId, await ChainIdOfAsync(f, w.W1.PublicId));

        // mover también los de otra cadena
        var p5 = await svc.PreviewBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", MoveFromOtherChains: true), default);
        var moved = await svc.ApplyBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", MoveFromOtherChains: true, Fingerprint: p5.Fingerprint), default);
        Assert.Equal((1, 2, 1, 0, 0), (moved.Assigned, moved.Moved, moved.AlreadyInChain, moved.SkippedOtherChain, moved.Excluded));
        Assert.Equal(4, (await svc.GetAsync(w.Walg.PublicId, default)).ClientCount);
    }

    [Fact]
    public async Task Bulk_apply_is_idempotent()
    {
        await using var f = await WmsFixture.CreateAsync();
        var svc = Svc(f);
        var w = await BulkWorldAsync(f, svc);

        var p1 = await svc.PreviewBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens"), default);
        var r1 = await svc.ApplyBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", Fingerprint: p1.Fingerprint), default);
        Assert.Equal((3, 0, 0, 1), (r1.Assigned, r1.Moved, r1.AlreadyInChain, r1.SkippedOtherChain));

        var p2 = await svc.PreviewBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens"), default);
        Assert.Equal((0, 3), (p2.ToAssign, p2.AlreadyInChain));
        var r2 = await svc.ApplyBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", Fingerprint: p2.Fingerprint), default);
        Assert.Equal((0, 0, 3, 1), (r2.Assigned, r2.Moved, r2.AlreadyInChain, r2.SkippedOtherChain));

        // la huella de la primera simulación ya no sirve
        var old = await Assert.ThrowsAsync<ConflictException>(() => svc.ApplyBulkAsync(w.Walg.PublicId, new ClientChainBulkRequest("walgreens", Fingerprint: p1.Fingerprint), default));
        Assert.Equal(R.FingerprintStale, old.Message);
    }
}
