using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Integrations;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>2026-10-24 — leer el buzón en tandas hasta vaciar los no leídos, con tope de correos y de tiempo, y sin repetir los mismos correos si marcar falla.</summary>
public class MailboxDrainTests
{
    /// <summary>Un buzón de mentira: los no leídos en orden (el más viejo primero); «marcar» los quita, salvo que se le diga que no puede.</summary>
    private sealed class FakeMailbox
    {
        public List<string> Unread { get; }
        public bool CanMark { get; set; } = true;
        public bool MarkThrows { get; set; }
        public List<string> Processed { get; } = new List<string>();
        public int Fetches { get; private set; }

        public FakeMailbox(int count) { Unread = Enumerable.Range(1, count).Select(i => "m" + i).ToList(); }

        public Task<IReadOnlyList<MailboxMessage>> Fetch(int size, CancellationToken ct)
        {
            Fetches++;
            IReadOnlyList<MailboxMessage> batch = Unread.Take(size).Select(k => new MailboxMessage(new MailMessageData(k, k, null, "a@b.com", null, new Dictionary<string, string>(), k, string.Empty, new List<MailAttachmentData>()), k)).ToList();
            return Task.FromResult(batch);
        }

        public Task Process(MailboxMessage m) { Processed.Add(m.Key); return Task.CompletedTask; }

        public Task Mark(IReadOnlyList<string> keys)
        {
            if (MarkThrows) throw new InvalidOperationException("solo lectura");
            if (CanMark) Unread.RemoveAll(k => keys.Contains(k));
            return Task.CompletedTask;
        }
    }

    private static Task<IntegrationRunner.MailDrainResult> Drain(FakeMailbox box, int max = 300, TimeSpan? budget = null, Func<DateTime>? now = null)
        => IntegrationRunner.DrainMailboxAsync(box.Fetch, box.Process, box.Mark, 50, max, budget ?? TimeSpan.FromMinutes(8), now ?? (() => DateTime.UtcNow), default);

    [Fact]
    public async Task Vacia_el_buzon_en_tandas_de_50_del_mas_viejo_al_mas_nuevo()
    {
        var box = new FakeMailbox(120);
        var r = await Drain(box);
        Assert.Equal(120, r.Messages);
        Assert.Equal(3, r.Batches);   // 50 + 50 + 20
        Assert.Null(r.Note);
        Assert.Equal("m1", box.Processed[0]);
        Assert.Equal("m120", box.Processed[119]);
        Assert.Empty(box.Unread);
    }

    [Fact]
    public async Task Un_buzon_con_exactamente_una_tanda_llena_pregunta_otra_vez_y_termina()
    {
        var box = new FakeMailbox(50);
        var r = await Drain(box);
        Assert.Equal(50, r.Messages);
        Assert.Null(r.Note);
        Assert.Equal(2, box.Fetches);   // la segunda vuelta encuentra el buzón vacío
    }

    [Fact]
    public async Task Respeta_el_tope_de_correos_por_corrida_y_deja_el_resto_para_la_siguiente()
    {
        var box = new FakeMailbox(400);
        var r = await Drain(box, max: 100);
        Assert.Equal(100, r.Messages);
        Assert.Equal(IntegrationRunner.MailTooManyNote, r.Note);
        Assert.Equal(300, box.Unread.Count);
    }

    [Fact]
    public async Task Respeta_el_tiempo_maximo_de_la_corrida()
    {
        var box = new FakeMailbox(400);
        var clock = DateTime.UtcNow;
        var r = await IntegrationRunner.DrainMailboxAsync(box.Fetch, async m => { await box.Process(m); clock = clock.AddMinutes(5); }, box.Mark, 50, 300, TimeSpan.FromMinutes(8), () => clock, default);
        Assert.Equal(IntegrationRunner.MailTooManyNote, r.Note);
        Assert.True(r.Batches >= 1 && box.Unread.Count > 0);
    }

    [Fact]
    public async Task Si_marcar_no_surte_efecto_deja_de_leer_en_vez_de_repetir_los_mismos_correos()
    {
        var box = new FakeMailbox(120) { CanMark = false };   // p. ej. la aplicación solo puede leer
        var r = await Drain(box);
        Assert.Equal(50, r.Messages);   // la segunda tanda trae los mismos 50: no se atienden otra vez
        Assert.Equal(IntegrationRunner.MailNotMarkedNote, r.Note);
        Assert.Equal(50, box.Processed.Count);
    }

    [Fact]
    public async Task Si_marcar_falla_avisa_y_no_sigue()
    {
        var box = new FakeMailbox(120) { MarkThrows = true };
        var r = await Drain(box);
        Assert.Equal(50, r.Messages);
        Assert.Equal(IntegrationRunner.MailNotMarkedNote, r.Note);
        Assert.Equal(1, r.Batches);
    }

    [Fact]
    public async Task Un_buzon_vacio_no_hace_nada()
    {
        var r = await Drain(new FakeMailbox(0));
        Assert.Equal(0, r.Messages);
        Assert.Equal(0, r.Batches);
        Assert.Null(r.Note);
    }
}
