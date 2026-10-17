using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Legal;
using Xunit;

namespace Teikem.Tests;

// Lote 99: documento legal único (privacidad + términos) y su aceptación por usuario.
public sealed class LegalTests
{
    private static LegalService Service(WmsFixture f, LegalOptions? o = null)
        => new(f.Db, f.Tenant, Options.Create(o ?? new LegalOptions()), new MemoryCache(new MemoryCacheOptions()), f.Get<Teikem.Infrastructure.Abstractions.ISecurityEventWriter>());

    [Fact]
    public void Document_has_both_parts_in_both_languages_with_operator_data_filled()
    {
        var o = new LegalOptions { OperatorName = "Acme Logistics LLC", ContactEmail = "privacidad@acme.test", OperatorAddress = "San Juan, PR" };
        foreach (var lang in new[] { "es", "en" })
        {
            var d = LegalText.Build(lang, o);
            Assert.Equal(LegalRules.CurrentVersion, d.Version);
            Assert.Contains(d.Sections, s => s.Part == "privacy");
            Assert.Contains(d.Sections, s => s.Part == "terms");
            Assert.Equal(d.Sections.Count, d.Sections.Select(s => s.Id).Distinct().Count());
            var all = string.Join(" ", d.Sections.SelectMany(s => s.Paragraphs)) + d.Intro;
            Assert.Contains("Acme Logistics LLC", all);
            Assert.Contains("privacidad@acme.test", all);
            Assert.DoesNotContain("{op}", all);
            Assert.DoesNotContain("{mail}", all);
            Assert.DoesNotContain("{addr}", all);
            Assert.DoesNotContain("{law}", all);
        }
        // mismas secciones en los dos idiomas
        Assert.Equal(LegalText.Build("es", o).Sections.Select(s => s.Id), LegalText.Build("en", o).Sections.Select(s => s.Id));
    }

    [Fact]
    public void Exempt_paths_keep_session_me_and_legal_open_but_not_the_rest_of_the_api()
    {
        Assert.True(LegalRules.IsExemptPath("/api/v1/auth/login"));
        Assert.True(LegalRules.IsExemptPath("/api/v1/auth/refresh"));
        Assert.True(LegalRules.IsExemptPath("/api/v1/legal/accept"));
        Assert.True(LegalRules.IsExemptPath("/api/v1/me"));
        Assert.True(LegalRules.IsExemptPath("/health"));
        Assert.False(LegalRules.IsExemptPath("/api/v1/orders"));
        Assert.False(LegalRules.IsExemptPath("/api/v1/me/pin"));
        Assert.False(LegalRules.IsExemptPath("/api/v1/clients/abc/notifications/settings"));
    }

    [Fact]
    public async Task A_user_who_never_accepted_is_pending_and_accepting_records_version_ip_and_agent()
    {
        await using var f = await WmsFixture.CreateAsync();
        f.Tenant.IpAddress = "10.0.0.5";
        f.Tenant.UserAgent = "TestAgent/1.0";
        var s = Service(f);
        var before = await s.StatusAsync(default);
        Assert.True(before.Required);
        Assert.False(before.Accepted);
        Assert.False(await s.HasAcceptedAsync(1, default));

        var after = await s.AcceptAsync(new LegalAcceptRequest(LegalRules.CurrentVersion, "en"), default);
        Assert.False(after.Required);
        Assert.True(after.Accepted);
        var row = Assert.Single(f.Db.LegalAcceptances);
        Assert.Equal((1, LegalRules.CurrentVersion, "en", "10.0.0.5", "TestAgent/1.0"), (row.UserId, row.Version, row.Lang, row.IpAddress, row.UserAgent));
        Assert.True(await s.HasAcceptedAsync(1, default));

        // aceptar dos veces no duplica
        await s.AcceptAsync(new LegalAcceptRequest(LegalRules.CurrentVersion, "es"), default);
        Assert.Single(f.Db.LegalAcceptances);
    }

    [Fact]
    public async Task Accepting_an_old_version_is_rejected_with_409_and_an_old_acceptance_does_not_count()
    {
        await using var f = await WmsFixture.CreateAsync();
        var s = Service(f);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.AcceptAsync(new LegalAcceptRequest("2020-01-01", "es"), default));
        Assert.Equal(LegalRules.VersionMismatchMessage, ex.Message);
        Assert.Empty(f.Db.LegalAcceptances);

        // quien aceptó una versión anterior vuelve a estar pendiente con la vigente
        f.Db.LegalAcceptances.Add(new LegalAcceptance { UserId = 1, Version = "2020-01-01", AcceptedAtUtc = DateTime.UtcNow.AddYears(-1) });
        await f.Db.SaveChangesAsync();
        var st = await s.StatusAsync(default);
        Assert.True(st.Required);
        Assert.False(st.Accepted);
        Assert.Equal("2020-01-01", st.AcceptedVersion);
    }

    [Fact]
    public async Task With_RequireAcceptance_off_nobody_is_required()
    {
        await using var f = await WmsFixture.CreateAsync();
        var s = Service(f, new LegalOptions { RequireAcceptance = false });
        Assert.False(s.Enforced);
        Assert.False((await s.StatusAsync(default)).Required);
    }

    [Fact]
    public async Task List_shows_who_accepted_and_who_did_not_only_for_the_active_company_pending_first()
    {
        await using var f = await WmsFixture.CreateAsync();
        ApplicationUser U(int id, string name) => new() { Id = id, UserName = name + "@x.co", Email = name + "@x.co", FullName = name, IsActive = true };
        f.Db.Users.AddRange(U(11, "Ana"), U(12, "Beto"), U(13, "Carla"), U(14, "Fuera"));
        f.Db.UserTenants.AddRange(
            new UserTenant { UserId = 11, TenantId = WmsFixture.TenantId }, new UserTenant { UserId = 12, TenantId = WmsFixture.TenantId },
            new UserTenant { UserId = 13, TenantId = WmsFixture.TenantId }, new UserTenant { UserId = 14, TenantId = WmsFixture.OtherTenantId });
        f.Db.LegalAcceptances.AddRange(
            new LegalAcceptance { UserId = 11, Version = LegalRules.CurrentVersion, AcceptedAtUtc = DateTime.UtcNow },
            new LegalAcceptance { UserId = 12, Version = "2020-01-01", AcceptedAtUtc = DateTime.UtcNow.AddYears(-1) });
        await f.Db.SaveChangesAsync();
        var s = Service(f);

        var all = await s.ListAsync(null, null, 0, 25, default);
        Assert.Equal(3, all.Total);
        Assert.Equal(new[] { "Beto", "Carla", "Ana" }, all.Items.Select(i => i.FullName));   // pendientes primero
        Assert.Equal("2020-01-01", all.Items.First(i => i.FullName == "Beto").LastVersion);
        Assert.DoesNotContain(all.Items, i => i.FullName == "Fuera");

        Assert.Equal(new[] { "Ana" }, (await s.ListAsync("accepted", null, 0, 25, default)).Items.Select(i => i.FullName));
        Assert.Equal(new[] { "Beto", "Carla" }, (await s.ListAsync("pending", null, 0, 25, default)).Items.Select(i => i.FullName));
        Assert.Equal(new[] { "Carla" }, (await s.ListAsync(null, "Carl", 0, 25, default)).Items.Select(i => i.FullName));
    }
}
