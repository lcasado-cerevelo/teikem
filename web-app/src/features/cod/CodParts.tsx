// COD (Lote 85): la lista de órdenes con COD, su ficha (cobros) y el registro de un cobro. Registrar cobros pide cod.collect; ver, cod.view.
import { useEffect, useMemo, useState } from 'react'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, Modal, Panel, QBox, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { IconCart } from '../../kernel/ui/screenIcons'
import {
  codOrderPageReader,
  useCodMutations,
  useCodOrder,
  useCodOrders,
  type CodCollectionDto,
  type CodOrderDto,
  type CodOrderListItemDto,
} from './codApi'
import { COD_METHODS, COD_STATUSES, collectFormError, needsCheckNumber, toCollectRequest, type CollectForm } from './codRules'

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

// ---------------------------------------------------------------- lista

export function CodOrdersTab({ onOpen }: { onOpen: (id: string) => void }) {
  const t = useT()
  const f = useFormat()
  const [status, setStatus] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const search = useDebounced(q.trim())
  const list = useCodOrders(status, search, (page - 1) * pageSize, pageSize)
  const columns = useMemo<DataColumn<CodOrderListItemDto>[]>(
    () => [
      { id: 'order', header: t('cod.col.order'), cell: (o) => <b>{o.orderNumber}</b>, exportValue: (o) => o.orderNumber ?? '', sortValue: (o) => o.orderNumber ?? '', card: 'title' },
      { id: 'invoice', header: t('cod.col.invoice'), cell: (o) => o.invoiceNumber ?? '—', exportValue: (o) => o.invoiceNumber ?? '', sortValue: (o) => o.invoiceNumber ?? '' },
      { id: 'client', header: t('cod.col.client'), cell: (o) => o.clientName, exportValue: (o) => o.clientName ?? '', sortValue: (o) => o.clientName ?? '' },
      { id: 'orderStatus', header: t('cod.col.orderStatus'), cell: (o) => o.orderStatusLabel, exportValue: (o) => o.orderStatusLabel ?? '', sortValue: (o) => o.orderStatusLabel ?? '' },
      { id: 'cod', header: t('cod.col.cod'), cell: (o) => f.money(o.codAmount ?? 0), exportValue: (o) => o.codAmount ?? 0, sortValue: (o) => o.codAmount ?? 0, align: 'end' },
      { id: 'collected', header: t('cod.col.collected'), cell: (o) => f.money(o.collected ?? 0), exportValue: (o) => o.collected ?? 0, sortValue: (o) => o.collected ?? 0, align: 'end' },
      { id: 'pending', header: t('cod.col.pending'), cell: (o) => f.money(o.pending ?? 0), exportValue: (o) => o.pending ?? 0, sortValue: (o) => o.pending ?? 0, align: 'end' },
      {
        id: 'status',
        header: t('cod.col.status'),
        cell: (o) => <Chip color={o.codStatusColor ?? undefined}>{o.codStatusLabel}</Chip>,
        exportValue: (o) => o.codStatusLabel ?? '',
        sortValue: (o) => o.codStatusLabel ?? '',
      },
    ],
    [t, f],
  )
  return (
    <Panel flush icon={<IconCart />} title={t('cod.orders.panel')} badge={list.isPending ? undefined : list.data?.total}>
      <div className="pb">
        <div className="qrow tax-filters">
          <QBox value={q} onChange={(v) => (setQ(v), setPage(1))} placeholder={t('cod.orders.search')} />
          <select aria-label={t('cod.orders.filterStatus')} value={status} onChange={(e) => (setStatus(e.target.value), setPage(1))}>
            <option value="">{t('cod.orders.allStatuses')}</option>
            {COD_STATUSES.map((s) => (
              <option key={s} value={s}>{t(`cod.statuses.${s}`)}</option>
            ))}
          </select>
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('cod.orders.panel')}
            columns={columns}
            rows={list.data?.items ?? []}
            rowKey={(o) => o.orderPublicId ?? ''}
            page={page}
            pageSize={pageSize}
            total={list.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(s) => (setPageSize(s), setPage(1))}
            onRowClick={(o) => onOpen(o.orderPublicId!)}
            empty={t('cod.orders.empty')}
            exportRows={() => fetchAllPages<CodOrderListItemDto>(codOrderPageReader(status, search))}
          />
        )}
      </div>
    </Panel>
  )
}

// ---------------------------------------------------------------- ficha

export function CodOrderModal({ id, onClose }: { id: string; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const order = useCodOrder(id)
  const [collecting, setCollecting] = useState(false)
  const data = order.data
  const columns = useMemo<DataColumn<CodCollectionDto>[]>(
    () => [
      { id: 'when', header: t('cod.col.when'), cell: (c) => f.dateTime(c.collectedAtUtc ?? ''), sortValue: (c) => c.collectedAtUtc ?? '', card: 'title' },
      { id: 'method', header: t('cod.col.method'), cell: (c) => c.methodLabel, sortValue: (c) => c.methodLabel ?? '' },
      { id: 'amount', header: t('cod.col.amount'), cell: (c) => f.money(c.amount ?? 0), sortValue: (c) => c.amount ?? 0, align: 'end' },
      { id: 'ref', header: t('cod.col.reference'), cell: (c) => [c.checkNumber, c.reference].filter(Boolean).join(' · ') || '—', sortValue: (c) => c.checkNumber ?? c.reference ?? '' },
      { id: 'status', header: t('cod.col.status'), cell: (c) => <Chip color={c.statusColor ?? undefined}>{c.statusLabel}</Chip>, sortValue: (c) => c.statusLabel ?? '' },
    ],
    [t, f],
  )
  const canCollect = data !== undefined && (data.pending ?? 0) > 0 && data.orderStatus !== 'SUBMITTED' && data.orderStatus !== 'CANCELLED'
  return (
    <Modal
      open
      size="lg"
      title={t('cod.order.title', { number: data?.orderNumber ?? '' })}
      onClose={onClose}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>{t('cod.close')}</button>
          {canCollect && (
            <Can perm="cod.collect">
              <button type="button" className="btn flow" onClick={() => setCollecting(true)}>{t('cod.collect')}</button>
            </Can>
          )}
        </>
      }
    >
      <div className="pb">
        {order.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(order.error).title}</p>
        ) : !data ? (
          <Spinner block />
        ) : (
          <>
            <dl className="cod-meta">
              <div><dt>{t('cod.col.status')}</dt><dd><Chip color={data.codStatusColor ?? undefined}>{data.codStatusLabel}</Chip></dd></div>
              <div><dt>{t('cod.col.client')}</dt><dd>{data.clientName}</dd></div>
              <div><dt>{t('cod.col.invoice')}</dt><dd>{data.invoiceNumber ?? '—'}</dd></div>
              <div><dt>{t('cod.col.orderStatus')}</dt><dd>{data.orderStatusLabel}</dd></div>
              <div><dt>{t('cod.col.cod')}</dt><dd>{f.money(data.codAmount ?? 0)}</dd></div>
              <div><dt>{t('cod.col.collected')}</dt><dd>{f.money(data.collected ?? 0)}</dd></div>
              <div><dt>{t('cod.col.pending')}</dt><dd>{f.money(data.pending ?? 0)}</dd></div>
            </dl>
            <h3 className="exp-h">{t('cod.collections')}</h3>
            <DataTable label={t('cod.collections')} columns={columns} rows={data.collections ?? []} rowKey={(c) => String(c.id)} empty={t('cod.noCollections')} />
          </>
        )}
      </div>
      {collecting && data && <CollectDialog order={data} onClose={() => setCollecting(false)} />}
    </Modal>
  )
}

// ---------------------------------------------------------------- cobrar

function CollectDialog({ order, onClose }: { order: CodOrderDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const { collect } = useCodMutations()
  const pending = order.pending ?? 0
  const [form, setForm] = useState<CollectForm>({ method: 'CASH', amount: String(pending), checkNumber: '', reference: '', notes: '' })
  const [error, setError] = useState<string | null>(null)
  const set = (patch: Partial<CollectForm>) => {
    setError(null)
    setForm((x) => ({ ...x, ...patch }))
  }
  const save = async () => {
    const problem = collectFormError(form, pending)
    if (problem) return setError(t(`cod.errors.${problem}`, { pending: f.money(pending) }))
    try {
      const saved = await collect.mutateAsync({ id: order.orderPublicId!, body: toCollectRequest(form) })
      toast.success(t('cod.collected', { number: saved.orderNumber ?? '' }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <Modal
      open
      title={t('cod.collectTitle', { number: order.orderNumber ?? '' })}
      onClose={onClose}
      dismissible={!collect.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={collect.isPending}>{t('cod.close')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={collect.isPending}>{collect.isPending ? t('common.loading') : t('cod.collectSave')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('cod.collectHelp', { pending: f.money(pending) })}</p>
        <div className="f">
          <label htmlFor="cod-method">{t('cod.col.method')}</label>
          <select id="cod-method" value={form.method} onChange={(e) => set({ method: e.target.value })}>
            {COD_METHODS.map((m) => (
              <option key={m} value={m}>{t(`cod.methods.${m}`)}</option>
            ))}
          </select>
        </div>
        <div className="f">
          <label htmlFor="cod-amount">{t('cod.col.amount')}</label>
          <input id="cod-amount" inputMode="decimal" value={form.amount} onChange={(e) => set({ amount: e.target.value })} />
        </div>
        {needsCheckNumber(form.method) && (
          <div className="f">
            <label htmlFor="cod-check">{t('cod.checkNumber')}</label>
            <input id="cod-check" value={form.checkNumber} maxLength={40} autoComplete="off" onChange={(e) => set({ checkNumber: e.target.value })} />
          </div>
        )}
        <div className="f">
          <label htmlFor="cod-ref">{t('cod.col.reference')}</label>
          <input id="cod-ref" value={form.reference} maxLength={80} autoComplete="off" onChange={(e) => set({ reference: e.target.value })} />
        </div>
        <div className="f">
          <label htmlFor="cod-notes">{t('cod.notes')}</label>
          <input id="cod-notes" value={form.notes} maxLength={300} autoComplete="off" onChange={(e) => set({ notes: e.target.value })} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}
