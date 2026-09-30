using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Lote 14 (P2, D14) — puente entre la bandeja de cambios de inventario de la petición (IInventoryChangeSink) y la cola de la
/// revisión en segundo plano. Alcance de petición, registrado junto al de auditoría. Reglas (hallazgo 11: RunInTransactionAsync
/// reintenta bajo la estrategia de reintentos y una llamada anidada se une a la transacción de afuera):
/// - Empieza una transacción → se VACÍA la bandeja: lo anotado en un intento anterior que falló (y se reintenta) no cuenta.
/// - Se confirma → se toma la bandeja y se manda a la cola (un aviso por tenant). Una transacción anidada no empieza ni
///   confirma nada propio: lo que anota se envía UNA vez, con el commit real de la de afuera.
/// - Se revierte o falla (commit o rollback con error) → se vacía la bandeja.
/// Si la cola no acepta (revisión apagada, sin consumidor en los comandos de consola o llena), lo anotado se descarta igual.
/// </summary>
public sealed class InventoryChangeCommitInterceptor(IInventoryChangeSink sink, InventoryReconciliationQueue queue) : DbTransactionInterceptor
{
    /// <summary>Una transacción empieza (o se reintenta): lo anotado antes no es de ella.</summary>
    public void OnTransactionStarted() => sink.Clear();

    /// <summary>Commit real: lo anotado sale a la cola; devuelve cuántos avisos aceptó.</summary>
    public int OnTransactionCommitted()
    {
        var accepted = 0;
        foreach (var notice in sink.Take())
            if (queue.TryEnqueue(notice)) accepted++;
        return accepted;
    }

    /// <summary>La transacción se revirtió o falló: nada de lo anotado ocurrió.</summary>
    public void OnTransactionAbandoned() => sink.Clear();

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        OnTransactionStarted();
        return base.TransactionStarted(connection, eventData, result);
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        OnTransactionStarted();
        return base.TransactionStartedAsync(connection, eventData, result, cancellationToken);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        OnTransactionCommitted();
        base.TransactionCommitted(transaction, eventData);
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        OnTransactionCommitted();
        return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        OnTransactionAbandoned();
        base.TransactionRolledBack(transaction, eventData);
    }

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        OnTransactionAbandoned();
        return base.TransactionRolledBackAsync(transaction, eventData, cancellationToken);
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        OnTransactionAbandoned();
        base.TransactionFailed(transaction, eventData);
    }

    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        OnTransactionAbandoned();
        return base.TransactionFailedAsync(transaction, eventData, cancellationToken);
    }
}
