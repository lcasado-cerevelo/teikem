// Lote 225 (P5, plan §1.1) — pestaña QuickBooks de Órdenes de venta (`/sales-orders?tab=quickbooks`). Las facturas de venta emitidas que faltan por pasar a QuickBooks
// Desktop (una sola marca «en QuickBooks»): «Exportar a QuickBooks» (sales.quickbooks.export) saca SIEMPRE todas las por pasar en un archivo .iif (pregunta si van los
// clientes y productos nuevos), lo baja y en la misma pantalla pregunta «¿Ya importaste estas N?» con la lista marcada; «Marcar como en QuickBooks» (sales.quickbooks.mark)
// marca una, varias o todas a mano. «Ver las ya pasadas» (`&qb=history`) abre el histórico con quién y cuándo, y deja quitar una marca puesta por error.
import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can, useCan } from '../../kernel/access'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import {
  Chip,
  ConfirmDialog,
  DataTable,
  DateRangeFilter,
  EMPTY_RANGE,
  Filters,
  IconCheckCircle,
  IconRotateCcw,
  Modal,
  Panel,
  QBox,
  Spinner,
  toast,
  type DataColumn,
  type RowAction,
} from '../../kernel/ui'
import { IconDoc } from '../../kernel/ui/screenIcons'
import {
  quickBooksHistoryPage,
  useQuickBooksHistory,
  useQuickBooksMutations,
  useQuickBooksPending,
  type QuickBooksExportDto,
  type QuickBooksHistoryItemDto,
  type QuickBooksMarkResultDto,
  type QuickBooksPendingDto,
  type QuickBooksPendingInvoiceDto,
} from './quickBooksApi'
import { downloadBase64File, filterPending, hasNewData, newDataOf } from './quickBooksRules'
import './quickbooks.css'

const NO_ROWS: never[] = []

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

function markedText(t: ReturnType<typeof useT>, r: QuickBooksMarkResultDto): string {
  const n = r.marked ?? 0
  return n === 1 ? t('sales.quickbooks.markedOne') : t('sales.quickbooks.marked', { n })
}

export function QuickBooksTab() {
  const [params, setParams] = useSearchParams()
  const history = params.get('qb') === 'history'
  const setHistory = (on: boolean) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (on) next.set('qb', 'history')
        else next.delete('qb')
        return next
      },
      { replace: true },
    )
  return history ? <HistoryView onBack={() => setHistory(false)} /> : <PendingView onHistory={() => setHistory(true)} />
}

function PendingView({ onHistory }: { onHistory: () => void }) {
  const t = useT()
  const f = useFormat()
  const canMark = useCan('sales.quickbooks.mark')
  const pending = useQuickBooksPending()
  const { exportFile, confirm, mark } = useQuickBooksMutations()
  const [q, setQ] = useState('')
  const [selected, setSelected] = useState<Set<string>>(() => new Set())
  const [asking, setAsking] = useState(false)
  const [exported, setExported] = useState<QuickBooksExportDto | null>(null)
  const [markAll, setMarkAll] = useState(false)
  const [details, setDetails] = useState<QuickBooksPendingInvoiceDto | null>(null)

  const all = pending.data?.invoices ?? NO_ROWS
  const rows = useMemo(() => filterPending(all, q), [all, q])
  // Lo elegido que sigue por pasar (lo ya marcado sale de la lista).
  const live = useMemo(() => all.filter((r) => selected.has(r.publicId ?? '')).map((r) => r.publicId!), [all, selected])
  const allShownChecked = rows.length > 0 && rows.every((r) => selected.has(r.publicId ?? ''))
  const toggle = (id: string, on: boolean) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (on) next.add(id)
      else next.delete(id)
      return next
    })
  const toggleShown = (on: boolean) =>
    setSelected((prev) => {
      const next = new Set(prev)
      for (const r of rows) if (on) next.add(r.publicId!)
      else next.delete(r.publicId!)
      return next
    })

  const runMark = async (ids: string[] | 'all') => {
    const r = await mark.mutateAsync(ids)
    toast.success(markedText(t, r))
    setSelected(new Set())
  }

  const columns = useMemo<DataColumn<QuickBooksPendingInvoiceDto>[]>(() => {
    const cols: DataColumn<QuickBooksPendingInvoiceDto>[] = []
    if (canMark)
      cols.push({
        id: 'select',
        header: t('sales.quickbooks.col.select'),
        exportable: false,
        cell: (r) => (
          <input
            type="checkbox"
            className="qb-check"
            checked={selected.has(r.publicId ?? '')}
            onChange={(e) => toggle(r.publicId!, e.target.checked)}
            aria-label={t('sales.quickbooks.selectRow', { number: r.number ?? '' })}
          />
        ),
      })
    cols.push(
      { id: 'date', header: t('sales.quickbooks.col.date'), cell: (r) => (r.issueDate ? f.date(r.issueDate) : '—'), exportValue: (r) => r.issueDate ?? '', sortValue: (r) => r.issueDate ?? '' },
      {
        id: 'number',
        header: t('sales.quickbooks.col.number'),
        card: 'title',
        cell: (r) => (
          <>
            <b>{r.number}</b>
            {(r.orderNumbers ?? []).length > 0 && <div className="help">{t('sales.quickbooks.orders', { list: (r.orderNumbers ?? []).join(', ') })}</div>}
          </>
        ),
        exportValue: (r) => r.number ?? '',
        sortValue: (r) => r.number ?? '',
      },
      {
        id: 'client',
        header: t('sales.quickbooks.col.client'),
        cell: (r) => (
          <>
            {r.clientName}
            {r.clientCode && <div className="help">{r.clientCode}</div>}
            {r.quickBooksCustomer && r.quickBooksCustomer !== r.clientName && <div className="help">{t('sales.quickbooks.inQuickBooksAs', { name: r.quickBooksCustomer })}</div>}
          </>
        ),
        exportValue: (r) => r.clientName ?? '',
        sortValue: (r) => r.clientName ?? '',
      },
      { id: 'total', header: t('sales.quickbooks.col.total'), cell: (r) => f.money(r.total ?? 0), exportValue: (r) => r.total ?? 0, sortValue: (r) => r.total ?? 0, align: 'end' },
      {
        id: 'news',
        header: t('sales.quickbooks.col.news'),
        cell: (r) =>
          hasNewData(r) ? (
            <span className="qb-news">
              {r.newClient && <Chip tone="warn">{t('sales.quickbooks.newClient')}</Chip>}
              {(r.newProductSkus ?? []).length > 0 && (
                <Chip tone="warn" title={(r.newProductSkus ?? []).join(', ')}>
                  {(r.newProductSkus ?? []).length === 1 ? t('sales.quickbooks.newProduct') : t('sales.quickbooks.newProducts', { n: (r.newProductSkus ?? []).length })}
                </Chip>
              )}
              <button type="button" className="linklike" onClick={() => setDetails(r)} aria-label={t('sales.quickbooks.seeDataOf', { number: r.number ?? '' })}>
                {t('sales.quickbooks.seeData')}
              </button>
            </span>
          ) : (
            '—'
          ),
        exportValue: (r) =>
          [r.newClient ? t('sales.quickbooks.newClient') : '', (r.newProductSkus ?? []).length > 0 ? `${t('sales.quickbooks.newProduct')}: ${(r.newProductSkus ?? []).join(', ')}` : ''].filter(Boolean).join(' · '),
        sortValue: (r) => (r.newClient ? 1 : 0) + (r.newProductSkus ?? []).length,
      },
      {
        id: 'exported',
        header: t('sales.quickbooks.col.exported'),
        cell: (r) => (r.exportedAtUtc ? <Chip tone="cap">{t('sales.quickbooks.exportedOn', { date: f.date(r.exportedAtUtc) })}</Chip> : '—'),
        exportValue: (r) => (r.exportedAtUtc ? f.dateTime(r.exportedAtUtc) : ''),
        sortValue: (r) => r.exportedAtUtc ?? '',
      },
    )
    return cols
  }, [t, f, canMark, selected])

  const rowActions: RowAction<QuickBooksPendingInvoiceDto>[] = [
    {
      key: 'mark',
      label: t('sales.quickbooks.markOne'),
      perm: 'sales.quickbooks.mark',
      icon: <IconCheckCircle />,
      onClick: (r) => {
        void runMark([r.publicId!]).catch((e) => toast.error(applyProblemDetails(e).title))
      },
    },
  ]

  const data = pending.data
  const count = data?.count ?? 0
  const already = data?.alreadyExportedCount ?? 0

  return (
    <>
      {exported && <ExportedPanel dto={exported} onDone={() => setExported(null)} confirming={confirm.isPending} onConfirm={(ids) => confirm.mutateAsync(ids)} />}
      <Panel
        flush
        icon={<IconDoc />}
        title={t('sales.quickbooks.panel')}
        badge={pending.isPending ? undefined : count}
        actions={
          <div className="qb-actions">
            <button type="button" className="btn sm" onClick={onHistory}>{t('sales.quickbooks.historyLink')}</button>
            <Can perm="sales.quickbooks.mark">
              <button type="button" className="btn sm" disabled={count === 0 || mark.isPending} onClick={() => setMarkAll(true)}>{t('sales.quickbooks.markAll')}</button>
            </Can>
            <Can perm="sales.quickbooks.export">
              <button type="button" className="btn sm money" disabled={count === 0 || exportFile.isPending} onClick={() => setAsking(true)}>{t('sales.quickbooks.export')}</button>
            </Can>
          </div>
        }
      >
        <div className="pb">
          {pending.isError ? (
            <p className="ferr" role="alert">{applyProblemDetails(pending.error).title}</p>
          ) : pending.isPending ? (
            <Spinner block />
          ) : (
            <>
              {already > 0 && (
                <p className="note qb-note" role="status">
                  {already === 1
                    ? t('sales.quickbooks.alreadyExportedOne', { date: data?.lastExportedAtUtc ? f.dateTime(data.lastExportedAtUtc) : '' })
                    : t('sales.quickbooks.alreadyExported', { n: already, date: data?.lastExportedAtUtc ? f.dateTime(data.lastExportedAtUtc) : '' })}
                </p>
              )}
              {(data?.warnings ?? []).length > 0 && (
                <div className="note qb-note qb-warn">
                  <b>{t('sales.quickbooks.warningsTitle')}</b>
                  <ul>
                    {(data?.warnings ?? []).map((w) => (
                      <li key={w}>{w}</li>
                    ))}
                  </ul>
                </div>
              )}
              <div className="qrow qb-tools">
                <QBox value={q} onChange={setQ} placeholder={t('sales.quickbooks.search')} />
                <Can perm="sales.quickbooks.mark">
                  <label className="qb-all">
                    <input type="checkbox" checked={allShownChecked} disabled={rows.length === 0} onChange={(e) => toggleShown(e.target.checked)} />
                    <span>{t('sales.quickbooks.selectAll', { n: rows.length })}</span>
                  </label>
                  <button
                    type="button"
                    className="btn sm flow"
                    disabled={live.length === 0 || mark.isPending}
                    onClick={() => void runMark(live).catch((e) => toast.error(applyProblemDetails(e).title))}
                  >
                    {t('sales.quickbooks.markSelected', { n: live.length })}
                  </button>
                </Can>
              </div>
              <DataTable
                label={t('sales.quickbooks.panel')}
                columns={columns}
                rows={rows}
                rowKey={(r) => r.publicId ?? ''}
                rowActions={rowActions}
                defaultSort={{ id: 'date', desc: false }}
                empty={q ? t('sales.quickbooks.noMatch') : t('sales.quickbooks.empty')}
              />
            </>
          )}
        </div>
      </Panel>
      {asking && data && (
        <ExportDialog
          pending={data}
          busy={exportFile.isPending}
          onClose={() => setAsking(false)}
          onExport={async (includeNew) => {
            const dto = await exportFile.mutateAsync(includeNew)
            downloadBase64File(dto.contentBase64 ?? '', dto.fileName ?? 'teikem-quickbooks.iif', dto.contentType)
            toast.success(t('sales.quickbooks.downloaded', { file: dto.fileName ?? '' }))
            setAsking(false)
            setExported(dto)
          }}
        />
      )}
      <ConfirmDialog
        open={markAll}
        title={t('sales.quickbooks.markAllTitle')}
        message={t('sales.quickbooks.markAllBody', { n: count })}
        confirmLabel={t('sales.quickbooks.markAll')}
        onConfirm={async () => {
          await runMark('all')
          setMarkAll(false)
        }}
        onClose={() => setMarkAll(false)}
      />
      {details && <NewDataModal invoice={details} pending={data} onClose={() => setDetails(null)} />}
    </>
  )
}

/** La pregunta de «Exportar»: ¿van los clientes y productos nuevos? Exportar no marca nada. */
function ExportDialog({ pending, busy, onClose, onExport }: { pending: QuickBooksPendingDto; busy: boolean; onClose: () => void; onExport: (includeNew: boolean) => Promise<void> }) {
  const t = useT()
  const f = useFormat()
  const clients = (pending.newClients ?? []).length
  const products = (pending.newProducts ?? []).length
  const [includeNew, setIncludeNew] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const go = async () => {
    setError(null)
    try {
      await onExport(includeNew)
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <Modal
      open
      title={t('sales.quickbooks.exportDialog.title')}
      onClose={onClose}
      dismissible={!busy}
      size="sm"
      footer={
        <>
          <button type="button" className="btn" disabled={busy} onClick={onClose}>{t('common.cancel')}</button>
          <button type="button" className="btn money" disabled={busy} onClick={() => void go()}>
            {busy ? t('sales.quickbooks.exportDialog.exporting') : t('sales.quickbooks.exportDialog.go')}
          </button>
        </>
      }
    >
      <p>{t('sales.quickbooks.exportDialog.body', { n: pending.count ?? 0, total: f.money(pending.total ?? 0) })}</p>
      {clients + products > 0 ? (
        <>
          <label className="qb-include">
            <input type="checkbox" checked={includeNew} onChange={(e) => setIncludeNew(e.target.checked)} />
            <span>{t('sales.quickbooks.exportDialog.includeNew')}</span>
          </label>
          <p className="help">{t('sales.quickbooks.exportDialog.includeNewHelp', { clients, products })}</p>
        </>
      ) : (
        <p className="help">{t('sales.quickbooks.exportDialog.noNew')}</p>
      )}
      {error && <p className="ferr" role="alert">{error}</p>}
    </Modal>
  )
}

/** Después de bajar el archivo: «¿Ya importaste estas N?» con la lista marcada; las desmarcadas salen otra vez en el próximo archivo. */
function ExportedPanel({ dto, onDone, onConfirm, confirming }: { dto: QuickBooksExportDto; onDone: () => void; onConfirm: (ids: string[]) => Promise<QuickBooksMarkResultDto>; confirming: boolean }) {
  const t = useT()
  const f = useFormat()
  const invoices = dto.invoices ?? NO_ROWS
  const [checked, setChecked] = useState<Set<string>>(() => new Set(invoices.map((i) => i.publicId!)))
  const [error, setError] = useState<string | null>(null)
  const ids = invoices.filter((i) => checked.has(i.publicId ?? '')).map((i) => i.publicId!)
  const allChecked = invoices.length > 0 && ids.length === invoices.length
  const confirmNow = async () => {
    setError(null)
    try {
      const r = await onConfirm(ids)
      toast.success(markedText(t, r))
      onDone()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <Panel icon={<IconCheckCircle />} title={t('sales.quickbooks.ask.title', { n: invoices.length })}>
      <p className="qb-summary">
        <b>{dto.fileName}</b> · {t('sales.quickbooks.ask.summary', { n: dto.invoiceCount ?? invoices.length, total: f.money(dto.total ?? 0) })} ·{' '}
        {dto.includedNew ? t('sales.quickbooks.ask.includedNew', { clients: dto.newClientCount ?? 0, products: dto.newProductCount ?? 0 }) : t('sales.quickbooks.ask.withoutNew')}
      </p>
      {(dto.alreadyExportedCount ?? 0) > 0 && (
        <p className="note qb-note">{t('sales.quickbooks.ask.alreadyBefore', { n: dto.alreadyExportedCount ?? 0, date: dto.lastExportedAtUtc ? f.dateTime(dto.lastExportedAtUtc) : '' })}</p>
      )}
      <p className="help">{t('sales.quickbooks.ask.help')}</p>
      <label className="qb-all">
        <input type="checkbox" checked={allChecked} onChange={(e) => setChecked(e.target.checked ? new Set(invoices.map((i) => i.publicId!)) : new Set())} />
        <span>{t('sales.quickbooks.ask.checkAll', { n: invoices.length })}</span>
      </label>
      <ul className="qb-ask-list" aria-label={t('sales.quickbooks.ask.listLabel')}>
        {invoices.map((i) => (
          <li key={i.publicId}>
            <label>
              <input
                type="checkbox"
                checked={checked.has(i.publicId ?? '')}
                onChange={(e) =>
                  setChecked((prev) => {
                    const next = new Set(prev)
                    if (e.target.checked) next.add(i.publicId!)
                    else next.delete(i.publicId!)
                    return next
                  })
                }
              />
              <span className="qb-ask-num">{i.number}</span>
              <span className="qb-ask-client">{i.clientName}</span>
              <span className="mono qb-ask-total">{f.money(i.total ?? 0)}</span>
            </label>
          </li>
        ))}
      </ul>
      {error && <p className="ferr" role="alert">{error}</p>}
      <div className="qb-actions qb-ask-actions">
        <button type="button" className="btn" onClick={() => downloadBase64File(dto.contentBase64 ?? '', dto.fileName ?? 'teikem-quickbooks.iif', dto.contentType)}>
          {t('sales.quickbooks.ask.again')}
        </button>
        <button type="button" className="btn" disabled={confirming} onClick={onDone}>{t('sales.quickbooks.ask.later')}</button>
        <button type="button" className="btn flow" disabled={ids.length === 0 || confirming} onClick={() => void confirmNow()}>
          {t('sales.quickbooks.ask.confirm', { n: ids.length })}
        </button>
      </div>
    </Panel>
  )
}

/** «ver datos»: el cliente y los productos nuevos de una factura, tal como salen en el archivo. */
function NewDataModal({ invoice, pending, onClose }: { invoice: QuickBooksPendingInvoiceDto; pending: QuickBooksPendingDto | undefined; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const { client, products } = newDataOf(invoice, pending)
  const fact = (label: string, value: string | null | undefined) => (
    <div key={label}>
      <dt>{label}</dt>
      <dd>{value && value.trim() ? value : '—'}</dd>
    </div>
  )
  return (
    <Modal open title={t('sales.quickbooks.newData.title', { number: invoice.number ?? '' })} onClose={onClose} footer={<button type="button" className="btn" onClick={onClose}>{t('sales.close')}</button>}>
      {client && (
        <section className="qb-data">
          <h3>{t('sales.quickbooks.newClient')}</h3>
          <dl className="sales-facts">
            {fact(t('sales.quickbooks.newData.qbName'), client.quickBooksName)}
            {fact(t('sales.quickbooks.newData.code'), client.code)}
            {fact(t('sales.quickbooks.newData.companyName'), client.companyName)}
            {fact(t('sales.quickbooks.newData.billTo'), (client.billTo ?? []).join(', '))}
            {fact(t('sales.quickbooks.newData.shipTo'), (client.shipTo ?? []).join(', '))}
            {fact(t('sales.quickbooks.newData.phone1'), client.phone1)}
            {fact(t('sales.quickbooks.newData.phone2'), client.phone2)}
            {fact(t('sales.quickbooks.newData.email'), client.email)}
            {fact(t('sales.quickbooks.newData.contact'), client.contact)}
            {fact(
              t('sales.quickbooks.newData.terms'),
              client.quickBooksTerms ?? (client.paymentTerm ? t('sales.quickbooks.newData.termsMissing', { code: client.paymentTerm }) : null),
            )}
            {fact(t('sales.quickbooks.newData.suri'), client.merchantRegistration)}
            {fact(t('sales.quickbooks.newData.creditLimit'), client.creditLimit != null ? f.money(client.creditLimit) : null)}
          </dl>
        </section>
      )}
      {products.length > 0 && (
        <section className="qb-data">
          <h3>{t('sales.quickbooks.newData.products')}</h3>
          <ul className="qb-products">
            {products.map((p) => (
              <li key={p.publicId}>
                <dl className="sales-facts">
                  {fact(t('sales.quickbooks.newData.qbItem'), p.quickBooksName)}
                  {fact(t('sales.quickbooks.newData.name'), p.name)}
                  {fact(t('sales.quickbooks.newData.price'), p.price != null ? f.money(p.price) : null)}
                  {fact(t('sales.quickbooks.newData.cost'), p.cost != null ? f.money(p.cost) : null)}
                  {fact(t('sales.quickbooks.newData.taxable'), p.taxable ? t('sales.quickbooks.newData.yes') : t('sales.quickbooks.newData.no'))}
                </dl>
              </li>
            ))}
          </ul>
        </section>
      )}
      {!client && products.length === 0 && <p className="help">{t('sales.quickbooks.newData.none')}</p>}
    </Modal>
  )
}

/** «Ver las ya pasadas»: el histórico (quién y cuándo), paginado por el servidor; quitar la marca con sales.quickbooks.mark. */
function HistoryView({ onBack }: { onBack: () => void }) {
  const t = useT()
  const f = useFormat()
  const [range, setRange] = useState(EMPTY_RANGE)
  const [q, setQ] = useState('')
  const search = useDebounced(q.trim())
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [toUnmark, setToUnmark] = useState<QuickBooksHistoryItemDto | null>(null)
  const { unmark } = useQuickBooksMutations()
  const filters = useMemo(() => ({ from: range.from || undefined, to: range.to || undefined, q: search || undefined }), [range, search])
  const query = useMemo(() => ({ ...filters, skip: (page - 1) * pageSize, take: pageSize }), [filters, page, pageSize])
  const list = useQuickBooksHistory(query)

  const columns = useMemo<DataColumn<QuickBooksHistoryItemDto>[]>(
    () => [
      { id: 'number', header: t('sales.quickbooks.col.number'), card: 'title', cell: (r) => <b>{r.number}</b>, exportValue: (r) => r.number ?? '', sortValue: (r) => r.number ?? '' },
      { id: 'date', header: t('sales.quickbooks.col.date'), cell: (r) => (r.issueDate ? f.date(r.issueDate) : '—'), exportValue: (r) => r.issueDate ?? '', sortValue: (r) => r.issueDate ?? '' },
      {
        id: 'client',
        header: t('sales.quickbooks.col.client'),
        cell: (r) => (
          <>
            {r.clientName}
            {r.clientCode && <div className="help">{r.clientCode}</div>}
          </>
        ),
        exportValue: (r) => r.clientName ?? '',
        sortValue: (r) => r.clientName ?? '',
      },
      { id: 'total', header: t('sales.quickbooks.col.total'), cell: (r) => f.money(r.total ?? 0), exportValue: (r) => r.total ?? 0, sortValue: (r) => r.total ?? 0, align: 'end' },
      { id: 'at', header: t('sales.quickbooks.history.at'), cell: (r) => f.dateTime(r.quickBooksAtUtc), exportValue: (r) => f.dateTime(r.quickBooksAtUtc), sortValue: (r) => r.quickBooksAtUtc ?? '' },
      { id: 'by', header: t('sales.quickbooks.history.by'), cell: (r) => r.quickBooksByName ?? '—', exportValue: (r) => r.quickBooksByName ?? '', sortValue: (r) => r.quickBooksByName ?? '' },
      {
        id: 'exported',
        header: t('sales.quickbooks.history.exported'),
        cell: (r) => (r.exportedAtUtc ? f.dateTime(r.exportedAtUtc) : t('sales.quickbooks.history.byHand')),
        exportValue: (r) => (r.exportedAtUtc ? f.dateTime(r.exportedAtUtc) : t('sales.quickbooks.history.byHand')),
        sortValue: (r) => r.exportedAtUtc ?? '',
      },
    ],
    [t, f],
  )

  return (
    <Panel
      flush
      icon={<IconDoc />}
      title={t('sales.quickbooks.history.panel')}
      badge={list.isPending ? undefined : list.data?.total}
      actions={<button type="button" className="btn sm" onClick={onBack}>{t('sales.quickbooks.history.back')}</button>}
    >
      <div className="pb">
        <Filters>
          <DateRangeFilter label={t('sales.quickbooks.history.markedRange')} value={range} onChange={(v) => (setRange(v), setPage(1))} />
        </Filters>
        <div className="qrow qb-tools">
          <QBox value={q} onChange={(v) => (setQ(v), setPage(1))} placeholder={t('sales.quickbooks.history.search')} />
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('sales.quickbooks.history.panel')}
            columns={columns}
            rows={list.data?.items ?? NO_ROWS}
            rowKey={(r) => r.publicId ?? ''}
            page={page}
            pageSize={pageSize}
            total={list.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(n) => (setPageSize(n), setPage(1))}
            exportRows={() => fetchAllPages<QuickBooksHistoryItemDto>(quickBooksHistoryPage(filters))}
            empty={t('sales.quickbooks.history.empty')}
            rowActions={[{ key: 'unmark', label: t('sales.quickbooks.history.unmark'), perm: 'sales.quickbooks.mark', icon: <IconRotateCcw />, onClick: (r) => setToUnmark(r) }]}
          />
        )}
      </div>
      <ConfirmDialog
        open={toUnmark !== null}
        title={t('sales.quickbooks.history.unmarkTitle', { number: toUnmark?.number ?? '' })}
        message={t('sales.quickbooks.history.unmarkBody')}
        confirmLabel={t('sales.quickbooks.history.unmark')}
        tone="danger"
        onConfirm={async () => {
          if (!toUnmark) return
          await unmark.mutateAsync([toUnmark.publicId!])
          toast.success(t('sales.quickbooks.history.unmarked', { number: toUnmark.number ?? '' }))
          setToUnmark(null)
        }}
        onClose={() => setToUnmark(null)}
      />
    </Panel>
  )
}
