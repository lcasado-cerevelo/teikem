// Lote 249 — el historial de modificaciones de una orden (quién la registró, quién la autorizó en el cliente y cómo, el cambio de total) y las facturas de la orden y de sus
// adiciones con su estado frente a QuickBooks. Cada fila abre la modificación con su «antes y después».
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useFormat } from '../../../kernel/format'
import { useT } from '../../../kernel/i18n'
import { Chip, DataTable, Spinner, type DataColumn } from '../../../kernel/ui'
import { ChangeDetailModal } from './ChangeDetailModal'
import { useOrderChanges, type ChangeListItemDto, type EditOptionsDto } from './changesApi'
import './changes.css'

type InvoiceRef = NonNullable<EditOptionsDto['invoices']>[number]

/** Las facturas de la orden y de sus adiciones, con su estado frente a QuickBooks (sin emitir, por exportar, exportada por confirmar, en QuickBooks). */
export function OrderInvoices({ invoices }: { invoices: readonly InvoiceRef[] }) {
  const t = useT()
  const f = useFormat()
  if (invoices.length === 0) return null
  return (
    <section aria-label={t('salesChanges.invoices.title')}>
      <h4>{t('salesChanges.invoices.title')}</h4>
      <ul className="soc-inv">
        {invoices.map((i) => (
          <li key={i.publicId}>
            <b className="mono">{i.number}</b>
            <Chip tone={i.status === 'VOID' ? 'neutral' : 'cod'}>{t(`billing.invoiceStatuses.${i.status}`)}</Chip>
            <Chip tone={i.quickBooksState === 'IN_QUICKBOOKS' ? 'deliv' : i.quickBooksState === 'EXPORTED_UNCONFIRMED' ? 'warn' : 'neutral'}>{t(`salesChanges.qb.${i.quickBooksState}`)}</Chip>
            <span className="help">{f.money(i.total ?? 0)}{i.isAddition ? ` · ${t('salesChanges.invoices.ofAddition', { number: i.orderNumber ?? '' })}` : ''}</span>
          </li>
        ))}
      </ul>
    </section>
  )
}

export function ChangeHistoryPanel({ orderPublicId }: { orderPublicId: string }) {
  const t = useT()
  const f = useFormat()
  const list = useOrderChanges(orderPublicId)
  const [openId, setOpenId] = useState<string | null>(null)
  const columns = useMemo<DataColumn<ChangeListItemDto>[]>(
    () => [
      { id: 'no', header: t('salesChanges.history.no'), cell: (c) => <b>{`#${c.changeNo}`}</b>, exportValue: (c) => c.changeNo ?? 0, sortValue: (c) => c.changeNo ?? 0, card: 'title' },
      { id: 'kind', header: t('salesChanges.history.kind'), cell: (c) => t(`salesChanges.kinds.${c.kind}`), sortValue: (c) => c.kind ?? '' },
      { id: 'status', header: t('sales.col.status'), cell: (c) => <Chip color={c.statusColor ?? undefined}>{c.statusLabel}</Chip>, exportValue: (c) => c.statusLabel ?? '', sortValue: (c) => c.statusLabel ?? '' },
      { id: 'when', header: t('salesChanges.history.when'), cell: (c) => f.dateTime(c.registeredAtUtc), exportValue: (c) => c.registeredAtUtc ?? '', sortValue: (c) => c.registeredAtUtc ?? '' },
      { id: 'by', header: t('salesChanges.history.by'), cell: (c) => c.registeredByName ?? '—', sortValue: (c) => c.registeredByName ?? '' },
      {
        id: 'authorized',
        header: t('salesChanges.history.authorized'),
        cell: (c) => (
          <>
            {c.authorizedByName}
            <span className="soc-sub"> · {[c.authorizedByTitle, c.channelLabel].filter(Boolean).join(' · ')}</span>
          </>
        ),
        exportValue: (c) => c.authorizedByName ?? '',
        sortValue: (c) => c.authorizedByName ?? '',
      },
      {
        id: 'delta',
        header: t('salesChanges.history.delta'),
        cell: (c) => f.money(c.totalDelta ?? 0, { signed: true }),
        exportValue: (c) => c.totalDelta ?? 0,
        sortValue: (c) => c.totalDelta ?? 0,
        align: 'end',
      },
    ],
    [t, f],
  )
  if (list.isPending) return <Spinner block />
  if (list.isError) return <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
  return (
    <div className="soc-hist">
      <DataTable
        label={t('salesChanges.history.title')}
        columns={columns}
        rows={list.data}
        rowKey={(c) => c.publicId ?? ''}
        pagination={false}
        exportable={false}
        defaultSort={{ id: 'no', desc: true }}
        onRowClick={(c) => setOpenId(c.publicId ?? null)}
        empty={t('salesChanges.history.empty')}
        rowActions={[{ key: 'open', label: t('salesChanges.history.open'), onClick: (c) => setOpenId(c.publicId ?? null) }]}
      />
      {openId && <ChangeDetailModal orderPublicId={orderPublicId} changePublicId={openId} onClose={() => setOpenId(null)} />}
    </div>
  )
}
