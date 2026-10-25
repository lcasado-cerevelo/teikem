using Teikem.Domain.Clients;
using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Un documento ya leído y convertido al contenido que el destino espera (CSV de la plantilla), con quién lo manda y en qué corrida. El runner lo entrega al destino del lector.
/// Template y Client son los del lector si los trae, y si no los de la integración. MailDate = el día (en la zona de la compañía) en que llegó el correo del que salió el documento (null si no vino de un correo):
/// los avisos lo usan como fecha cuando el documento no trae una.
/// </summary>
public sealed record DocumentTargetRequest(Integration Integration, ImportTemplate Template, Client Client, IntegrationRun Run, string FileName, byte[] Content, bool CheckDuplicate, DocumentReader? Reader = null, bool Doubtful = false, DateOnly? MailDate = null);

/// <summary>
/// Un DESTINO de lo que lee una integración (2026-10-22): lo que crea en el sistema un documento leído. Cada destino se registra por su código (<see cref="DocumentTargets"/>) y recibe el contenido ya
/// leído; el runner no sabe qué crea. Etapa 1: ORDER (orden de transporte, por el importador de órdenes y su plantilla). Agregar un destino (aviso de llegada, de salida, orden de compra, de venta,
/// productos, factura procesada…) es agregar una clase que implemente esta interfaz y registrarla, sin tocar el runner ni los lectores.
/// </summary>
public interface IDocumentTarget
{
    string Kind { get; }
    Task<FileOutcome> ProcessAsync(DocumentTargetRequest request, CancellationToken ct);
}

/// <summary>Destino ORDER: el contenido pasa por la plantilla de importación de órdenes (el camino de siempre del integrador).</summary>
internal sealed class OrderDocumentTarget(Func<DocumentTargetRequest, CancellationToken, Task<FileOutcome>> process) : IDocumentTarget
{
    public string Kind => DocumentTargets.Order;
    public Task<FileOutcome> ProcessAsync(DocumentTargetRequest request, CancellationToken ct) => process(request, ct);
}
