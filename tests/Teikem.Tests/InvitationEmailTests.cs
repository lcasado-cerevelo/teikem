using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-22 — La invitación al portal (la del usuario del cliente y la de su consignatario) sale de verdad por correo: enlace al portal del ambiente
/// (Portal:WebBaseUrl) con el correo y el token, remitente con el nombre de la compañía, y sin dirección del portal o con el proveedor caído no se rompe la invitación.
/// </summary>
public sealed class InvitationEmailTests
{
    private sealed class FakeEmail : ITransactionalEmailSender
    {
        public bool IsConfigured => true;
        public bool Fail { get; set; }
        public List<(string To, string Subject, string Html, string? Company)> Sent { get; } = new();
        public Task SendAsync(string toEmail, string? toName, string subject, string htmlContent, CancellationToken ct, string? companyName = null)
        {
            if (Fail) throw new InvalidOperationException("rechazado");
            Sent.Add((toEmail, subject, htmlContent, companyName));
            return Task.CompletedTask;
        }
    }

    private static IConfiguration Config(string? portalUrl)
        => new ConfigurationBuilder().AddInMemoryCollection(portalUrl is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["Portal:WebBaseUrl"] = portalUrl }).Build();

    private static async Task<(EmailInvitationSender Sender, FakeEmail Email)> CreateAsync(WmsFixture f, string? portalUrl)
    {
        (await f.Db.Tenants.FindAsync(WmsFixture.TenantId) ?? f.Db.Tenants.Add(new Tenant { TenantId = WmsFixture.TenantId, IsActive = true }).Entity).Name = "Advance Logistics";
        await f.Db.SaveChangesAsync();
        var email = new FakeEmail();
        return (new EmailInvitationSender(email, Config(portalUrl), f.Get<ITenantContext>(), f.Db, NullLogger<EmailInvitationSender>.Instance), email);
    }

    [Fact]
    public void The_link_goes_to_the_portal_with_the_email_and_the_token_encoded()
    {
        Assert.Equal("https://test-portal.advancelogisticspr.com/accept-invite?email=ana%2Bprueba%40cliente.co&token=a%2Fb%3D%3D",
            InvitationEmail.Link("https://test-portal.advancelogisticspr.com/", "ana+prueba@cliente.co", "a/b=="));
    }

    [Fact]
    public void The_text_escapes_the_names_people_type()
    {
        var html = InvitationEmail.Html("https://p.test/accept-invite?email=a&token=b", "<b>ACME</b> & Cia", "Advance <Logistics>", new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc));
        Assert.DoesNotContain("<b>ACME</b>", html);
        Assert.Contains("&lt;b&gt;ACME&lt;/b&gt; &amp; Cia", html);
        Assert.Contains("Advance &lt;Logistics&gt;", html);
        Assert.Contains("2026-10-24 12:00 UTC", html);
        Assert.Contains("email=a&amp;token=b", html);   // el & del enlace va escapado dentro del atributo
    }

    [Fact]
    public async Task Sends_the_invitation_with_the_company_name_as_sender()
    {
        await using var f = await WmsFixture.CreateAsync();
        var (sender, email) = await CreateAsync(f, "https://portal.test");

        await sender.SendInviteAsync("ana@cliente.co", "tok/en", "AxisCare", DateTime.UtcNow.AddHours(48), default);

        var sent = Assert.Single(email.Sent);
        Assert.Equal(("ana@cliente.co", InvitationEmail.Subject, "Advance Logistics"), (sent.To, sent.Subject, sent.Company));
        Assert.Contains("https://portal.test/accept-invite?email=ana%40cliente.co&amp;token=tok%2Fen", sent.Html);
        Assert.Contains("AxisCare", sent.Html);
    }

    [Fact]
    public async Task Without_the_portal_address_nothing_is_sent_and_nothing_breaks()
    {
        await using var f = await WmsFixture.CreateAsync();
        var (sender, email) = await CreateAsync(f, null);

        await sender.SendInviteAsync("ana@cliente.co", "tok", "AxisCare", DateTime.UtcNow.AddHours(48), default);

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task A_provider_failure_does_not_break_the_invitation()
    {
        await using var f = await WmsFixture.CreateAsync();
        var (sender, email) = await CreateAsync(f, "https://portal.test");
        email.Fail = true;

        await sender.SendInviteAsync("ana@cliente.co", "tok", "AxisCare", DateTime.UtcNow.AddHours(48), default);   // no lanza

        Assert.Empty(email.Sent);
    }
}
