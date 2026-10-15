using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Teikem.Domain.Identity;
using Teikem.Domain.Messaging;
using Teikem.Domain.Security;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Messaging;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 53 — mensajería contra SQL Server REAL (la unicidad del hilo entre dos bajo concurrencia y las consultas de no leídos lo necesitan).
/// Base DESECHABLE propia (Teikem_msgtest_&lt;guid&gt;, se borra al terminar), creada desde el modelo EF y sin llaves foráneas (se siembran usuarios,
/// roles y compañías sueltos); los índices únicos SÍ quedan. Se omite sin TEIKEM_TEST_SQLSERVER.
/// </summary>
public sealed class MessagingSqlFixture : IAsyncLifetime
{
    public string? ConnectionString { get; private set; }
    private string? _database;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(TenantIsolationSqlServerFixture.EnvVar);
        if (string.IsNullOrWhiteSpace(server)) return;
        _database = "Teikem_msgtest_" + Guid.NewGuid().ToString("N")[..12];
        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = _database }.ConnectionString;
        try
        {
            await using var db = NewDb(new TenantContext());
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @sql nvarchar(max) = N'';
                SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
                  FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id = f.parent_object_id;
                EXEC sp_executesql @sql;
                """);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public TeikemDbContext NewDb(ITenantContext tenant) => new(new DbContextOptionsBuilder<TeikemDbContext>()
        .UseSqlServer(ConnectionString)
        .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)).Options, tenant);

    public async Task<int> NewTenantAsync(string name)
    {
        await using var db = NewDb(new TenantContext());
        var t = new Tenant { Name = name, DefaultLangCode = "es" };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.TenantId;
    }

    public async Task DisposeAsync()
    {
        if (_database is null) return;
        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(TenantIsolationSqlServerFixture.EnvVar)) { InitialCatalog = "master" }.ConnectionString;
        await using var c = new SqlConnection(master);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"IF DB_ID('{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END";
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>Un empuje en vivo capturado.</summary>
public sealed class CapturingLiveNotifier : IMessageLiveNotifier
{
    public sealed record Push(int TenantId, int[] UserIds, string Event, MessageLiveEvent Payload);
    private readonly object _gate = new();
    public List<Push> Pushes { get; } = new();

    public Task PushAsync(int tenantId, IReadOnlyCollection<int> userIds, string eventName, MessageLiveEvent payload, CancellationToken ct)
    {
        lock (_gate) Pushes.Add(new Push(tenantId, userIds.OrderBy(i => i).ToArray(), eventName, payload));
        return Task.CompletedTask;
    }
}

internal static class MessagingTestExtensions
{
    /// <summary>El contexto de compañía con el que se creó el DbContext de prueba.</summary>
    public static ITenantContext TenantOf(this TeikemDbContext db)
        => (ITenantContext)typeof(TeikemDbContext).GetField("_tenant", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(db)!;
}

public sealed class MessagingSqlTests(MessagingSqlFixture fx) : IClassFixture<MessagingSqlFixture>, IAsyncLifetime
{
    private static int _seed = 1000;
    private static int NextId() => Interlocked.Increment(ref _seed);
    private int A;
    private int B;
    private readonly CapturingLiveNotifier _live = new();

    public async Task InitializeAsync()
    {
        if (fx.ConnectionString is null) return;
        (A, B) = (await fx.NewTenantAsync("Compañía A"), await fx.NewTenantAsync("Compañía B"));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- siembra ----

    private async Task<int> UserAsync(int tenantId, string name, bool active = true, string? email = null)
    {
        await using var db = fx.NewDb(new TenantContext { TenantId = tenantId });
        var mail = email ?? $"{name.ToLowerInvariant().Replace(' ', '.')}{NextId()}@x.com";
        var u = new ApplicationUser { UserName = mail, Email = mail, FullName = name, IsActive = active };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        db.UserTenants.Add(new UserTenant { UserId = u.Id, TenantId = tenantId, StatusCodeId = 1 });
        await db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<int> RoleAsync(int tenantId, string name, params int[] userIds)
    {
        await using var db = fx.NewDb(new TenantContext { TenantId = tenantId });
        var r = new Role { TenantId = tenantId, Name = name + NextId() };
        db.AppRoles.Add(r);
        await db.SaveChangesAsync();
        foreach (var uid in userIds) db.AppUserRoles.Add(new UserRole { UserId = uid, RoleId = r.RoleId, TenantId = tenantId });
        await db.SaveChangesAsync();
        return r.RoleId;
    }

    private (MessagingService Svc, TeikemDbContext Db) As(int tenantId, int userId)
    {
        var db = fx.NewDb(new TenantContext { TenantId = tenantId, UserId = userId });
        return (new MessagingService(db, db.TenantOf(), new PresenceService(db), _live), db);
    }

    private static async Task<ValidationException> Invalid(Func<Task> act) => await Assert.ThrowsAsync<ValidationException>(act);

    private async Task<Guid> DirectAsync(int tenantId, int from, int to, string text = "hola")
    {
        var (svc, db) = As(tenantId, from);
        await using var _ = db;
        return (await svc.CreateThreadAsync(new MessageThreadRequest(new[] { to }, null, null, text), default)).Thread.PublicId;
    }

    // ---- hilo entre dos ----

    [SqlServerFact]
    public async Task A_message_to_one_person_makes_a_direct_thread_that_the_other_sees_unread_and_can_answer()
    {
        var ana = await UserAsync(A, "Ana Rivera");
        var luis = await UserAsync(A, "Luis Casado");
        var thread = await DirectAsync(A, ana, luis, "  Hola Luis  ");

        var (luisSvc, ldb) = As(A, luis);
        await using var _l = ldb;
        var list = await luisSvc.ListThreadsAsync(default);
        var t = Assert.Single(list);
        Assert.Equal((thread, "DIRECT", "Ana Rivera", 1, "Hola Luis"), (t.PublicId, t.Kind, t.Name, t.Unread, t.LastMessagePreview));
        Assert.Equal(ana, t.Other!.Id);
        Assert.Equal(1, (await luisSvc.UnreadAsync(default)).Count);

        var open = await luisSvc.GetThreadAsync(thread, null, 50, default);
        var m = Assert.Single(open.Messages);
        Assert.Equal(("Hola Luis", false), (m.Body, m.Mine));
        Assert.False(open.CanAddMembers);
        await luisSvc.MarkReadAsync(thread, default);
        Assert.Equal(0, (await luisSvc.UnreadAsync(default)).Count);

        // contesta: ahora es Ana quien tiene uno sin leer, y el hilo es el MISMO aunque Luis «escriba» de nuevo desde cero
        await luisSvc.SendAsync(thread, new MessageSendRequest("Hola Ana"), default);
        var again = await luisSvc.CreateThreadAsync(new MessageThreadRequest(new[] { ana }, null, null, "otra vez"), default);
        Assert.Equal(thread, again.Thread.PublicId);
        var (anaSvc, adb) = As(A, ana);
        await using var _a = adb;
        var anaList = Assert.Single(await anaSvc.ListThreadsAsync(default));
        Assert.Equal((2, "Luis Casado"), (anaList.Unread, anaList.Name));
        Assert.Equal(new[] { "Hola Luis", "Hola Ana", "otra vez" }, (await anaSvc.GetThreadAsync(thread, null, 50, default)).Messages.Select(x => x.Body));
    }

    [SqlServerFact]
    public async Task Writing_to_the_same_person_at_once_never_makes_two_threads()
    {
        var ana = await UserAsync(A, "Ana");
        var luis = await UserAsync(A, "Luis");
        var ids = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
        {
            var (svc, db) = As(A, i % 2 == 0 ? ana : luis);
            await using var _ = db;
            try { return (Guid?)(await svc.CreateThreadAsync(new MessageThreadRequest(new[] { i % 2 == 0 ? luis : ana }, null, null, $"m{i}"), default)).Thread.PublicId; }
            catch (Exception) { return null; }
        })));
        await using var db2 = fx.NewDb(new TenantContext { TenantId = A });
        Assert.Equal(1, await db2.MessageThreads.CountAsync(t => t.DirectKey == MessagingRules.DirectKey(ana, luis)));
        Assert.All(ids, i => Assert.NotNull(i));   // nadie falla: el que pierde la carrera usa el hilo del ganador
        Assert.Single(ids.Distinct());
    }

    // ---- grupos y roles ----

    [SqlServerFact]
    public async Task A_message_to_several_people_makes_a_group_named_after_them_and_everyone_can_answer()
    {
        var ana = await UserAsync(A, "Ana");
        var luis = await UserAsync(A, "Luis");
        var pedro = await UserAsync(A, "Pedro");
        var rosa = await UserAsync(A, "Rosa");
        var (svc, db) = As(A, ana);
        await using var _ = db;
        var group = await svc.CreateThreadAsync(new MessageThreadRequest(new[] { luis, pedro, rosa, luis }, null, null, "Reunión de mañana"), default);
        Assert.Equal(("GROUP", "Luis, Pedro, +1", 4, true), (group.Thread.Kind, group.Thread.Name, group.Thread.MemberCount, group.CanAddMembers));

        var (pedroSvc, pdb) = As(A, pedro);
        await using var _p = pdb;
        var theirs = Assert.Single(await pedroSvc.ListThreadsAsync(default));
        Assert.Equal(("Ana, Luis, +1", 1), (theirs.Name, theirs.Unread));
        await pedroSvc.SendAsync(group.Thread.PublicId, new MessageSendRequest("Ahí estaré"), default);
        var (rosaSvc, rdb) = As(A, rosa);
        await using var _r = rdb;
        Assert.Equal(2, Assert.Single(await rosaSvc.ListThreadsAsync(default)).Unread);
        // con título propio
        var titled = await svc.CreateThreadAsync(new MessageThreadRequest(new[] { luis, pedro }, null, "  Equipo  ", "hola"), default);
        Assert.Equal("Equipo", titled.Thread.Name);
    }

    [SqlServerFact]
    public async Task A_message_to_a_role_makes_a_group_with_the_active_people_who_have_it_today_as_a_snapshot()
    {
        var ana = await UserAsync(A, "Ana");
        var gerente1 = await UserAsync(A, "Gerente Uno");
        var gerente2 = await UserAsync(A, "Gerente Dos");
        var inactivo = await UserAsync(A, "Gerente Inactivo", active: false);
        var role = await RoleAsync(A, "Gerencia", gerente1, gerente2, inactivo, ana);
        var (svc, db) = As(A, ana);
        await using var _ = db;
        var group = await svc.CreateThreadAsync(new MessageThreadRequest(null, role, null, "Cierre de mes"), default);
        Assert.Equal(("GROUP", 3), (group.Thread.Kind, group.Thread.MemberCount));   // Ana (quien escribe) + los dos gerentes activos
        Assert.StartsWith("Gerencia", group.Thread.Name);
        Assert.DoesNotContain(group.Members, m => m.UserId == inactivo);

        // quien entra al rol después NO entra solo
        var nuevo = await UserAsync(A, "Gerente Nuevo");
        await using (var rdb = fx.NewDb(new TenantContext { TenantId = A }))
        {
            rdb.AppUserRoles.Add(new UserRole { UserId = nuevo, RoleId = role, TenantId = A });
            await rdb.SaveChangesAsync();
        }
        var (nuevoSvc, ndb) = As(A, nuevo);
        await using var _n = ndb;
        Assert.Empty(await nuevoSvc.ListThreadsAsync(default));
        // pero se le puede agregar a mano, y ve solo lo que se escriba desde que entra
        await svc.SendAsync(group.Thread.PublicId, new MessageSendRequest("Antes de que entrara"), default);
        await svc.AddMembersAsync(group.Thread.PublicId, new MessageMembersRequest(new[] { nuevo }), default);
        await svc.SendAsync(group.Thread.PublicId, new MessageSendRequest("Ya con el nuevo"), default);
        var seen = await nuevoSvc.GetThreadAsync(group.Thread.PublicId, null, 50, default);
        Assert.Equal(new[] { "Ya con el nuevo" }, seen.Messages.Select(m => m.Body));
        Assert.Equal(1, (await nuevoSvc.UnreadAsync(default)).Count);
    }

    [SqlServerFact]
    public async Task Validations_use_the_exact_messages()
    {
        var ana = await UserAsync(A, "Ana");
        var luis = await UserAsync(A, "Luis");
        var otro = await UserAsync(B, "De otra compañía");
        var vacio = await RoleAsync(A, "Vacío");
        var deB = await RoleAsync(B, "Rol de B", otro);
        var (svc, db) = As(A, ana);
        await using var _ = db;
        MessageThreadRequest R(int[]? users, int? role = null, string? text = "hola", string? title = null) => new(users, role, title, text);

        Assert.Equal("Escriba el mensaje.", (await Invalid(() => svc.CreateThreadAsync(R(new[] { luis }, text: "  "), default))).Message);
        Assert.Equal("El mensaje no puede pasar de 2000 caracteres.", (await Invalid(() => svc.CreateThreadAsync(R(new[] { luis }, text: new string('x', 2001)), default))).Message);
        Assert.Equal("El nombre del grupo no puede pasar de 100 caracteres.", (await Invalid(() => svc.CreateThreadAsync(R(new[] { luis }, title: new string('n', 101)), default))).Message);
        Assert.Equal("Escoja al menos una persona o un rol.", (await Invalid(() => svc.CreateThreadAsync(R(null), default))).Message);
        Assert.Equal("Escoja al menos una persona o un rol.", (await Invalid(() => svc.CreateThreadAsync(R(Array.Empty<int>()), default))).Message);
        Assert.Equal("No puede escribirse a sí mismo.", (await Invalid(() => svc.CreateThreadAsync(R(new[] { ana }), default))).Message);
        Assert.Equal("Usuario no encontrado en esta compañía.", (await Invalid(() => svc.CreateThreadAsync(R(new[] { otro }), default))).Message);
        Assert.Equal("Usuario no encontrado en esta compañía.", (await Invalid(() => svc.CreateThreadAsync(R(new[] { 999999 }), default))).Message);
        Assert.Equal("Rol no encontrado.", (await Invalid(() => svc.CreateThreadAsync(R(null, deB), default))).Message);
        Assert.Equal("Rol no encontrado.", (await Invalid(() => svc.CreateThreadAsync(R(null, 999999), default))).Message);
        Assert.Equal("Ese rol no tiene personas activas a quienes escribir.", (await Invalid(() => svc.CreateThreadAsync(R(null, vacio), default))).Message);
        await using var check = fx.NewDb(new TenantContext { TenantId = A });
        Assert.Equal(0, await check.MessageThreads.CountAsync());   // nada se creó a medias
    }

    // ---- no leídos, silencio y aislamiento ----

    [SqlServerFact]
    public async Task Unread_counts_only_other_peoples_messages_and_reading_clears_them_and_the_latest_is_reported()
    {
        var ana = await UserAsync(A, "Ana");
        var luis = await UserAsync(A, "Luis");
        var pedro = await UserAsync(A, "Pedro");
        var t1 = await DirectAsync(A, luis, ana, "uno de Luis");
        var t2 = await DirectAsync(A, pedro, ana, "uno de Pedro");
        var (svc, db) = As(A, ana);
        await using var _ = db;
        await svc.SendAsync(t1, new MessageSendRequest("mío"), default);   // lo propio nunca cuenta
        await svc.SendAsync(t2, new MessageSendRequest("mío también"), default);
        var (lsvc, ldb) = As(A, luis);
        await using var _l = ldb;
        await lsvc.SendAsync(t1, new MessageSendRequest("dos de Luis"), default);

        var unread = await svc.UnreadAsync(default);
        Assert.Equal((1, 1), (unread.Count, unread.Threads));   // contestar es haber leído: lo anterior a su respuesta ya no cuenta; solo «dos de Luis»
        Assert.Equal(("Luis", "dos de Luis"), (unread.Latest!.FromName, unread.Latest.Preview));
        await svc.MarkReadAsync(t1, default);
        await svc.MarkReadAsync(t2, default);
        Assert.Equal(new MessageUnreadDto(0, 0, null), await svc.UnreadAsync(default));
    }

    [SqlServerFact]
    public async Task A_muted_thread_still_counts_as_unread_but_its_push_is_flagged_muted()
    {
        var ana = await UserAsync(A, "Ana");
        var luis = await UserAsync(A, "Luis");
        var thread = await DirectAsync(A, ana, luis);
        var (lsvc, ldb) = As(A, luis);
        await using var _l = ldb;
        Assert.True((await lsvc.MuteAsync(thread, new MessageMuteRequest(), default)).IsMuted);
        _live.Pushes.Clear();
        var (svc, db) = As(A, ana);
        await using var _ = db;
        await svc.SendAsync(thread, new MessageSendRequest("¿me oyes?"), default);
        Assert.Equal(2, (await lsvc.UnreadAsync(default)).Count);   // el primero y «¿me oyes?»: silenciar no apaga el contador
        Assert.True((await lsvc.UnreadAsync(default)).Latest!.IsMuted);
        var toLuis = Assert.Single(_live.Pushes, p => p.Event == MessageLiveEvents.MessageReceived);
        Assert.Equal(new[] { luis }, toLuis.UserIds);
        Assert.True(toLuis.Payload.Muted);
        Assert.False((await lsvc.MuteAsync(thread, new MessageMuteRequest(false), default)).IsMuted);
    }

    [SqlServerFact]
    public async Task Someone_who_is_not_in_a_thread_or_is_in_another_company_gets_not_found_for_everything()
    {
        var ana = await UserAsync(A, "Ana");
        var luis = await UserAsync(A, "Luis");
        var intruso = await UserAsync(A, "Intruso");
        var deB = await UserAsync(B, "De B");
        var thread = await DirectAsync(A, ana, luis);
        foreach (var (tenant, user) in new[] { (A, intruso), (B, deB) })
        {
            var (svc, db) = As(tenant, user);
            await using var _ = db;
            Assert.Empty(await svc.ListThreadsAsync(default));
            foreach (var act in new Func<Task>[]
            {
                () => svc.GetThreadAsync(thread, null, 50, default),
                () => svc.SendAsync(thread, new MessageSendRequest("¿puedo?"), default),
                () => svc.MarkReadAsync(thread, default),
                () => svc.AddMembersAsync(thread, new MessageMembersRequest(new[] { user }), default),
                () => svc.LeaveAsync(thread, default),
                () => svc.MuteAsync(thread, new MessageMuteRequest(), default),
            })
            {
                var nf = await Assert.ThrowsAsync<NotFoundException>(act);
                Assert.Equal("Conversación no encontrada.", nf.Message);
            }
        }
        var (asvc, adb) = As(A, ana);
        await using var _a = adb;
        Assert.Single((await asvc.GetThreadAsync(thread, null, 50, default)).Messages);   // nada se coló
    }

    // ---- administrar un grupo ----

    [SqlServerFact]
    public async Task Only_the_creator_adds_people_a_direct_thread_cannot_grow_or_be_left_and_leaving_a_group_hides_it()
    {
        var ana = await UserAsync(A, "Ana");
        var luis = await UserAsync(A, "Luis");
        var pedro = await UserAsync(A, "Pedro");
        var rosa = await UserAsync(A, "Rosa");
        var (svc, db) = As(A, ana);
        await using var _ = db;
        var group = (await svc.CreateThreadAsync(new MessageThreadRequest(new[] { luis, pedro }, null, "Grupo", "hola"), default)).Thread.PublicId;
        var direct = await DirectAsync(A, ana, rosa);

        var (lsvc, ldb) = As(A, luis);
        await using var _l = ldb;
        var forbidden = await Assert.ThrowsAsync<ForbiddenException>(() => lsvc.AddMembersAsync(group, new MessageMembersRequest(new[] { rosa }), default));
        Assert.Equal("Solo quien creó el grupo puede agregar personas.", forbidden.Message);
        Assert.Equal("A una conversación entre dos personas no se pueden agregar más; cree un grupo.", (await Assert.ThrowsAsync<ConflictException>(() => svc.AddMembersAsync(direct, new MessageMembersRequest(new[] { pedro }), default))).Message);
        Assert.Equal("Una conversación entre dos personas no se abandona.", (await Assert.ThrowsAsync<ConflictException>(() => svc.LeaveAsync(direct, default))).Message);
        Assert.Equal("Indique a quién agregar.", (await Invalid(() => svc.AddMembersAsync(group, new MessageMembersRequest(Array.Empty<int>()), default))).Message);
        Assert.Equal("Usuario no encontrado en esta compañía.", (await Invalid(() => svc.AddMembersAsync(group, new MessageMembersRequest(new[] { 999999 }), default))).Message);

        // Luis se sale: el hilo deja de existir para él, y los demás lo siguen viendo
        await lsvc.LeaveAsync(group, default);
        Assert.Empty(await lsvc.ListThreadsAsync(default));
        await Assert.ThrowsAsync<NotFoundException>(() => lsvc.SendAsync(group, new MessageSendRequest("sigo aquí"), default));
        Assert.Equal(2, (await svc.GetThreadAsync(group, null, 50, default)).Thread.MemberCount);
        // y vuelve si el creador lo agrega: no ve lo escrito mientras estuvo fuera
        await svc.SendAsync(group, new MessageSendRequest("mientras estabas fuera"), default);
        await svc.AddMembersAsync(group, new MessageMembersRequest(new[] { luis }), default);
        Assert.Empty((await lsvc.GetThreadAsync(group, null, 50, default)).Messages);
        Assert.Equal(0, (await lsvc.UnreadAsync(default)).Count);
    }

    [SqlServerFact]
    public async Task Long_threads_come_in_pages_from_the_newest_back()
    {
        var ana = await UserAsync(A, "Ana");
        var luis = await UserAsync(A, "Luis");
        var thread = await DirectAsync(A, ana, luis, "m1");
        var (svc, db) = As(A, ana);
        await using var _ = db;
        for (var i = 2; i <= 60; i++) await svc.SendAsync(thread, new MessageSendRequest($"m{i}"), default);
        var first = await svc.GetThreadAsync(thread, null, 50, default);
        Assert.Equal((50, true, "m11", "m60"), (first.Messages.Count, first.HasMore, first.Messages[0].Body, first.Messages[^1].Body));
        var older = await svc.GetThreadAsync(thread, first.Messages[0].Id, 50, default);
        Assert.Equal((10, false, "m1", "m10"), (older.Messages.Count, older.HasMore, older.Messages[0].Body, older.Messages[^1].Body));
    }

    // ---- en vivo ----

    [SqlServerFact]
    public async Task Each_message_is_pushed_live_to_the_others_after_it_is_saved_and_a_failed_one_pushes_nothing()
    {
        var ana = await UserAsync(A, "Ana");
        var luis = await UserAsync(A, "Luis");
        var pedro = await UserAsync(A, "Pedro");
        var (svc, db) = As(A, ana);
        await using var _ = db;
        _live.Pushes.Clear();
        await Invalid(() => svc.CreateThreadAsync(new MessageThreadRequest(new[] { luis }, null, null, "  "), default));
        Assert.Empty(_live.Pushes);

        var group = (await svc.CreateThreadAsync(new MessageThreadRequest(new[] { luis, pedro }, null, "G", "hola a todos"), default)).Thread.PublicId;
        var received = Assert.Single(_live.Pushes, p => p.Event == MessageLiveEvents.MessageReceived);
        Assert.Equal(A, received.TenantId);
        Assert.Equal(new[] { luis, pedro }, received.UserIds);
        Assert.Equal(("Ana", "hola a todos", group, false), (received.Payload.SenderName, received.Payload.Preview, received.Payload.ThreadPublicId, received.Payload.Muted));
        var own = Assert.Single(_live.Pushes, p => p.Event == MessageLiveEvents.ThreadChanged);
        Assert.Equal(new[] { ana }, own.UserIds);   // las otras ventanas de quien escribió

        _live.Pushes.Clear();
        var (lsvc, ldb) = As(A, luis);
        await using var _l = ldb;
        await lsvc.MarkReadAsync(group, default);
        Assert.Equal(MessageLiveEvents.ThreadRead, _live.Pushes.Single().Event);
        Assert.Equal(new[] { luis }, _live.Pushes.Single().UserIds);
        _live.Pushes.Clear();
        await svc.AddMembersAsync(group, new MessageMembersRequest(new[] { await UserAsync(A, "Rosa") }), default);
        Assert.Equal(MessageLiveEvents.ThreadChanged, _live.Pushes.Single().Event);
        Assert.Equal(4, _live.Pushes.Single().UserIds.Length);
    }

    // ---- directorio y presencia ----

    [SqlServerFact]
    public async Task The_people_directory_lists_active_people_of_the_company_with_presence_and_filters_by_name_or_email()
    {
        var ana = await UserAsync(A, "Ana Rivera", email: $"ana.rivera{NextId()}@advance.com");
        var luis = await UserAsync(A, "Luis Casado");
        var inactivo = await UserAsync(A, "Luis Inactivo", active: false);
        var deB = await UserAsync(B, "Luis de B");
        await using (var db1 = fx.NewDb(new TenantContext { TenantId = A, UserId = luis }))
            await new PresenceService(db1).PingAsync(luis, DateTime.UtcNow, default);
        var (svc, db) = As(A, ana);
        await using var _ = db;
        var all = await svc.PeopleAsync(null, 50, default);
        Assert.DoesNotContain(all, p => p.Id == ana || p.Id == inactivo || p.Id == deB);   // ni ella misma, ni inactivos, ni de otra compañía
        var l = Assert.Single(all, p => p.Id == luis);
        Assert.True(l.Online);
        Assert.NotNull(l.LastSeenUtc);
        Assert.Equal(new[] { luis }, (await svc.PeopleAsync("casado", 20, default)).Select(p => p.Id));
        Assert.Contains((await svc.PeopleAsync("@x.com", 20, default)), p => p.Id == luis);

        // visto hace rato: ya no está en línea
        await using (var db2 = fx.NewDb(new TenantContext()))
            await db2.UserPresences.Where(p => p.UserId == luis).ExecuteUpdateAsync(s => s.SetProperty(p => p.LastSeenUtc, DateTime.UtcNow.AddMinutes(-5)));
        var stale = Assert.Single(await svc.PeopleAsync("casado", 20, default));
        Assert.False(stale.Online);
        Assert.NotNull(stale.LastSeenUtc);
        // y se ve también en el hilo
        var thread = await DirectAsync(A, ana, luis);
        Assert.False((await svc.GetThreadAsync(thread, null, 50, default)).Members.Single(m => m.UserId == luis).Online);
    }

    [SqlServerFact]
    public async Task Roles_come_with_how_many_active_people_each_has_in_this_company_only()
    {
        var ana = await UserAsync(A, "Ana");
        var uno = await UserAsync(A, "Uno");
        var inactivo = await UserAsync(A, "Dos", active: false);
        var role = await RoleAsync(A, "Ventas", ana, uno, inactivo);
        await RoleAsync(B, "De otra compañía");
        var (svc, db) = As(A, ana);
        await using var _ = db;
        var roles = await svc.RolesAsync(default);
        Assert.Equal(2, roles.Single(r => r.Id == role).MemberCount);
        Assert.DoesNotContain(roles, r => r.Name.StartsWith("De otra compañía"));
    }

    [SqlServerFact]
    public async Task Presence_is_renewed_in_place_and_two_first_pings_at_once_do_not_fail()
    {
        var ana = await UserAsync(A, "Ana");
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = fx.NewDb(new TenantContext());
            await new PresenceService(db).PingAsync(ana, DateTime.UtcNow, default);
        }));
        await using var check = fx.NewDb(new TenantContext());
        Assert.Equal(1, await check.UserPresences.CountAsync(p => p.UserId == ana));
        var before = (await check.UserPresences.AsNoTracking().SingleAsync(p => p.UserId == ana)).LastSeenUtc;
        await new PresenceService(check).PingAsync(ana, before.AddSeconds(30), default);
        Assert.Equal(before.AddSeconds(30), (await check.UserPresences.AsNoTracking().SingleAsync(p => p.UserId == ana)).LastSeenUtc, TimeSpan.FromMilliseconds(5));
    }
}
