using Xunit;
using DispatchBatchRules = Teikem.Domain.Trips.DispatchBatchRules;

namespace Teikem.Tests;

/// <summary>Lote 5 (P4): selección del despacho en lote, resumen y mensajes exactos de despacho y salida.</summary>
public class DispatchBatchRulesTests
{
    [Fact]
    public void Validate_requires_at_least_one_trip()
    {
        Assert.Equal("Seleccione al menos una ruta.", DispatchBatchRules.Validate(null).Error);
        Assert.Equal("Seleccione al menos una ruta.", DispatchBatchRules.Validate(Array.Empty<Guid>()).Error);
        Assert.Equal("Seleccione al menos una ruta.", DispatchBatchRules.Validate(new[] { Guid.Empty }).Error);
    }

    [Fact]
    public void Validate_caps_the_batch_at_50_distinct_trips()
    {
        var fifty = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();
        var ok = DispatchBatchRules.Validate(fifty);
        Assert.Null(ok.Error);
        Assert.Equal(50, ok.Ids.Count);

        var fiftyOne = fifty.Append(Guid.NewGuid()).ToList();
        Assert.Equal("Máximo 50 rutas por despacho.", DispatchBatchRules.Validate(fiftyOne).Error);

        // Los repetidos no cuentan dos veces.
        var withDuplicates = fifty.Concat(fifty.Take(10)).ToList();
        Assert.Null(DispatchBatchRules.Validate(withDuplicates).Error);
    }

    [Fact]
    public void Validate_collapses_duplicates_keeping_arrival_order()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var (ids, error) = DispatchBatchRules.Validate(new[] { b, a, b, a });
        Assert.Null(error);
        Assert.Equal(new[] { b, a }, ids);
    }

    [Fact]
    public void Summarize_counts_dispatched_and_failed()
    {
        var s = DispatchBatchRules.Summarize(new[] { true, false, true, false, false });
        Assert.Equal(5, s.Requested);
        Assert.Equal(2, s.Dispatched);
        Assert.Equal(3, s.Failed);

        var empty = DispatchBatchRules.Summarize(Array.Empty<bool>());
        Assert.Equal(0, empty.Requested);
        Assert.Equal(0, empty.Dispatched);
    }

    [Fact]
    public void Start_and_pipeline_messages_are_exact()
    {
        Assert.Equal("La ruta 2026-0007 no está despachada; despáchela antes de registrar su salida.",
            DispatchBatchRules.NotDispatchedForStart("2026-0007"));
        Assert.Equal("La ruta 2026-0007 ya salió.", DispatchBatchRules.AlreadyStarted("2026-0007"));
        Assert.Equal("El pipeline de rutas de esta compañía no tiene habilitada la etapa DISPATCHED.",
            DispatchBatchRules.PipelineMissing("DISPATCHED"));
        Assert.Equal("El pipeline de rutas de esta compañía no tiene habilitada la etapa ACTIVE.",
            DispatchBatchRules.PipelineMissing("ACTIVE"));
    }

    [Fact]
    public void Dispatch_blocked_message_joins_reasons_without_their_final_period()
    {
        var msg = DispatchBatchRules.DispatchBlocked("2026-0003", new[]
        {
            "La ruta no tiene vehículo asignado.",
            "Orden 2026-000123: El estatus actual no permite la acción 'ASSIGN_TRIP'.",
        });
        Assert.Equal("La ruta 2026-0003 no se puede despachar: La ruta no tiene vehículo asignado; "
                     + "Orden 2026-000123: El estatus actual no permite la acción 'ASSIGN_TRIP'.", msg);
    }

    [Fact]
    public void Effect_messages_are_exact()
    {
        Assert.Equal("Orden 2026-000001: La orden no tiene una parada de entrega pendiente.",
            DispatchBatchRules.OrderNotEligible("2026-000001", "La orden no tiene una parada de entrega pendiente."));
        Assert.Equal("Despachada en la ruta 2026-0001", DispatchBatchRules.OrderDispatchedComment("2026-0001"));
        Assert.Equal("Salió en la ruta 2026-0001", DispatchBatchRules.OrderStartedComment("2026-0001"));
        Assert.Equal("La ruta 2026-0001 ya fue despachada; no se puede eliminar.", DispatchBatchRules.CannotDeleteDispatched("2026-0001"));
        Assert.Equal("La orden va en la ruta 2026-0001 ya despachada; no se puede cancelar mientras la ruta esté en curso.",
            DispatchBatchRules.CannotCancelOrderInDispatchedTrip("2026-0001"));
        Assert.Equal("Salida registrada", DispatchBatchRules.DefaultStartComment);
        Assert.Equal("Secuencia manual confirmada al despachar", DispatchBatchRules.ManualSequenceComment);
        Assert.Equal("Despachada", DispatchBatchRules.RouteDispatchedComment);
        Assert.Equal("Ruta no encontrada.", DispatchBatchRules.TripNotFoundMessage);
    }
}
