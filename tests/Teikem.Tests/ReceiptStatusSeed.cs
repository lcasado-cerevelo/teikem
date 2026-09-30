using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Tests;

/// <summary>
/// Lote 13 — entradas laterales del recibo (RECEIPT) como las siembra logistica-db-seed.sql (bloque 3G): DISCREPANCY desde
/// RECEIVING; RECEIVED_VARIANCE desde RECEIVING, DISCREPANCY y EXPECTED; PUTAWAY desde RECEIVED_VARIANCE. Lo usan los
/// fixtures InMemory que siembran el dominio ReceiptStatus.
/// </summary>
internal static class ReceiptStatusSeed
{
    public static readonly (string Lateral, string From)[] Entries =
    {
        (ReceiptStatuses.Discrepancy, ReceiptStatuses.Receiving),
        (ReceiptStatuses.ReceivedWithVariance, ReceiptStatuses.Receiving),
        (ReceiptStatuses.ReceivedWithVariance, ReceiptStatuses.Discrepancy),
        (ReceiptStatuses.ReceivedWithVariance, ReceiptStatuses.Expected),
        (ReceiptStatuses.Putaway, ReceiptStatuses.ReceivedWithVariance),
    };

    public static void AddLateralEntries(TeikemDbContext db, int receiptEntityTypeId, Func<string, string, int> statusId, int firstId = 900)
    {
        var id = firstId;
        foreach (var (lateral, from) in Entries)
            db.StatusLateralEntries.Add(new StatusLateralEntry
            {
                StatusLateralEntryId = id++, TenantId = null, EntityTypeLookupId = receiptEntityTypeId,
                LateralStatusCodeId = statusId(StatusDomains.ReceiptStatus, lateral),
                FromStatusCodeId = statusId(StatusDomains.ReceiptStatus, from), IsAllowed = true,
            });
    }
}
