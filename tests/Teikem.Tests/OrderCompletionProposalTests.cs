using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-23: cola de PROPUESTAS de cierre (lectores ORDER_COMPLETION, p. ej. la hoja «Ruta completada» de CMS). El documento nombra las órdenes por su número; nada cambia solo: SUGGEST deja una propuesta por confirmar,
/// AUTO la aplica en el acto, y confirmar o rechazar lo hace una persona. OrderService, StatusService y los servicios de la cola reales sobre InMemory.
/// </summary>
public sealed class OrderCompletionProposalTests
{
    private static async Task<OrderSubmissionLifecycleTests.World> WorldAsync()
    {
        var x = await OrderSubmissionLifecycleTests.NewWorldAsync(extra: s =>
        {
            s.AddSingleton<DeliveryRegistrationService>();
            s.AddSingleton<OrderCompletionApplier>();
            s.AddSingleton<OrderCompletionDocumentTarget>();
            s.AddSingleton<IntegrationProposalService>();
        });
        // InMemory no aplica el DEFAULT NEWID() de PublicId: se asigna al guardar, como haría SQL Server
        x.Db.SavingChanges += (_, _) =>
        {
            foreach (var e in x.Db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added))
                if (e.Metadata.FindProperty("PublicId") is { ClrType: var t } p && t == typeof(Guid) && (Guid)e.Property(p.Name).CurrentValue! == Guid.Empty)
                    e.Property(p.Name).CurrentValue = Guid.NewGuid();
        };
        x.Db.Integrations.Add(new Integration { IntegrationId = 1, TenantId = WmsFixture.TenantId, Name = "CMS cierres", ServiceUserId = 1 });
        x.Db.IntegrationRuns.Add(new IntegrationRun { IntegrationRunId = 1, TenantId = WmsFixture.TenantId, IntegrationId = 1 });
        await x.Db.SaveChangesAsync();
        x.Db.ChangeTracker.Clear();
        return x;
    }

    private static async Task<string> NewOrderAsync(OrderSubmissionLifecycleTests.World x)
    {
        var dto = await x.Orders.CreateAsync(new OrderCreateRequest(x.C.PublicId, x.Consignee.PublicId, null, null, "STANDARD", null,
            new List<OrderPackageLineRequest> { new("BOX", "Cama") }, null, null, null, null, null, null, null, OverrideCredit: true), OrderScope.Any, null, default);
        x.Db.ChangeTracker.Clear();
        return dto.OrderNumber;
    }

    private static DocumentTargetRequest Request(OrderSubmissionLifecycleTests.World x, string csv, string mode = ReaderModes.Suggest, string? toStatus = null, string file = "ruta-completada.csv")
        => new(new Integration { IntegrationId = 1, TenantId = WmsFixture.TenantId, Name = "CMS cierres", ServiceUserId = 1 },
            new ImportTemplate { HasHeader = false, Delimiter = "," }, x.C, new IntegrationRun { IntegrationRunId = 1, TenantId = WmsFixture.TenantId, IntegrationId = 1 },
            file, Encoding.UTF8.GetBytes(csv), CheckDuplicate: true, Reader: new DocumentReader("Cierres", ReaderSources.Pdf, DocumentTargets.OrderCompletion, Mode: mode, ToStatus: toStatus));

    private static async Task<string> StatusOfAsync(OrderSubmissionLifecycleTests.World x, string orderNumber)
        => await x.Db.TransportOrders.AsNoTracking().Where(o => o.OrderNumber == orderNumber).Select(o => o.Status!.InternalCode).FirstAsync();

    [Fact]
    public async Task Suggest_mode_leaves_a_pending_proposal_and_changes_nothing()
    {
        var x = await WorldAsync();
        var number = await NewOrderAsync(x);
        var outcome = await x.F.Get<OrderCompletionDocumentTarget>().ProcessAsync(Request(x, $"{number},PU\n{number},PU\n"), default);

        Assert.Single(outcome.Created);   // la clave repetida en el documento es UNA propuesta
        Assert.Empty(outcome.Rejected);
        Assert.Equal(OrderStatuses.Submitted, await StatusOfAsync(x, number));
        var proposal = await x.Db.IntegrationProposals.AsNoTracking().SingleAsync();
        Assert.Equal(ProposalStatuses.Pending, proposal.Status);
        Assert.Equal(CompletionTargets.Pickup, proposal.ToStatus);
        Assert.Equal(number, proposal.ReferenceKey);
        Assert.Contains("ruta-completada.csv", proposal.Summary);
        Assert.Contains("1 propuesta(s) por confirmar", outcome.Log.Message);
    }

    [Fact]
    public async Task Confirming_moves_the_order_by_the_status_service_in_the_name_of_the_person_and_cannot_be_repeated()
    {
        var x = await WorldAsync();
        var number = await NewOrderAsync(x);
        await x.F.Get<OrderCompletionDocumentTarget>().ProcessAsync(Request(x, $"{number}\n"), default);
        var service = x.F.Get<IntegrationProposalService>();
        var listed = await service.ListAsync(null, 0, 50, default);
        Assert.Equal(1, listed.Pending);
        var item = Assert.Single(listed.Items);
        Assert.Equal(number, item.OrderNumber);

        var done = await service.ConfirmAsync(item.PublicId, new ProposalDecisionRequest("Verificado con CMS"), default);
        Assert.Equal(ProposalStatuses.Confirmed, done.Status);
        Assert.Equal("Verificado con CMS", done.Note);
        Assert.NotNull(done.DecidedAtUtc);
        Assert.Equal(OrderStatuses.Pickup, await StatusOfAsync(x, number));
        Assert.Equal(0, (await service.ListAsync(null, 0, 50, default)).Pending);

        await Assert.ThrowsAsync<ConflictException>(() => service.ConfirmAsync(item.PublicId, null, default));
        await Assert.ThrowsAsync<ConflictException>(() => service.RejectAsync(item.PublicId, null, default));
    }

    [Fact]
    public async Task Rejecting_leaves_the_order_as_it_was()
    {
        var x = await WorldAsync();
        var number = await NewOrderAsync(x);
        await x.F.Get<OrderCompletionDocumentTarget>().ProcessAsync(Request(x, $"{number}\n"), default);
        var service = x.F.Get<IntegrationProposalService>();
        var item = Assert.Single((await service.ListAsync(ProposalStatuses.Pending, 0, 50, default)).Items);
        var rejected = await service.RejectAsync(item.PublicId, new ProposalDecisionRequest("No es de esa ruta"), default);
        Assert.Equal(ProposalStatuses.Rejected, rejected.Status);
        Assert.Equal(OrderStatuses.Submitted, await StatusOfAsync(x, number));
    }

    [Fact]
    public async Task Auto_mode_applies_at_once_in_the_name_of_the_service_account()
    {
        var x = await WorldAsync();
        var number = await NewOrderAsync(x);
        var outcome = await x.F.Get<OrderCompletionDocumentTarget>().ProcessAsync(Request(x, $"{number}\n", ReaderModes.Auto), default);
        Assert.Empty(outcome.Rejected);
        Assert.Equal(OrderStatuses.Pickup, await StatusOfAsync(x, number));
        var proposal = await x.Db.IntegrationProposals.AsNoTracking().SingleAsync();
        Assert.Equal(ProposalStatuses.Applied, proposal.Status);
        Assert.Equal(1, proposal.DecidedBy);
        Assert.Contains("1 orden(es) cerrada(s)", outcome.Log.Message);
    }

    [Fact]
    public async Task An_unknown_order_goes_to_the_tray_and_a_second_document_does_not_repeat_a_live_proposal()
    {
        var x = await WorldAsync();
        var number = await NewOrderAsync(x);
        var target = x.F.Get<OrderCompletionDocumentTarget>();
        var first = await target.ProcessAsync(Request(x, $"{number}\n999999\n"), default);
        var missing = Assert.Single(first.Rejected);
        Assert.Equal(OrderCompletionDocumentTarget.OrderNotFoundMessage("999999", x.C.Name), missing.Reason);
        Assert.Single(first.Created);

        var second = await target.ProcessAsync(Request(x, $"{number}\n", file: "otra.csv"), default);
        Assert.Empty(second.Created);
        Assert.Contains("1 ya estaban propuestas", second.Log.Message);
        Assert.Equal(1, await x.Db.IntegrationProposals.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task An_order_already_past_the_pickup_is_confirmed_without_touching_it_and_the_same_file_is_not_read_twice()
    {
        var x = await WorldAsync();
        var number = await NewOrderAsync(x);
        var target = x.F.Get<OrderCompletionDocumentTarget>();
        await x.F.Get<OrderCompletionApplier>().ApplyAsync((await x.Db.TransportOrders.AsNoTracking().FirstAsync(o => o.OrderNumber == number)).TransportOrderId, CompletionTargets.Pickup, "antes", default);
        x.Db.ChangeTracker.Clear();
        await target.ProcessAsync(Request(x, $"{number}\n"), default);
        var service = x.F.Get<IntegrationProposalService>();
        var item = Assert.Single((await service.ListAsync(null, 0, 50, default)).Items);
        var done = await service.ConfirmAsync(item.PublicId, null, default);
        Assert.Equal(ProposalStatuses.Confirmed, done.Status);
        Assert.Equal("La orden ya estaba en ese estatus.", done.Message);
    }

    [Fact]
    public void Reader_mode_and_closing_status_are_validated_with_exact_messages()
    {
        DocumentReader R(string mode, string? to) => new("Cierres", ReaderSources.Pdf, DocumentTargets.OrderCompletion, null, [new EmailBodyField(1, EmailBodyKinds.Label, "A")], Mode: mode, ToStatus: to);
        Assert.Empty(DocumentReaderRules.Validate([R(ReaderModes.Auto, CompletionTargets.Delivered)]));
        Assert.Empty(DocumentReaderRules.Validate([R(ReaderModes.Suggest, null)]));
        Assert.Equal(DocumentReaderRules.UnknownModeMessage("YA"), DocumentReaderRules.Validate([R("YA", null)])["email.readers[0].mode"]);
        Assert.Equal(DocumentReaderRules.UnknownCompletionMessage("CERRADA"), DocumentReaderRules.Validate([R(ReaderModes.Suggest, "CERRADA")])["email.readers[0].toStatus"]);
    }
}
