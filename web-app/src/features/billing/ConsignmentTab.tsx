// Pestaña «Consignación» de Facturación (Lote 224, P6; módulo BILLING): facturas con producto dejado en consignación (dejado, devuelto, pagado y saldo),
// el reporte «Consignado por cliente», registrar un pago (billing.consignment.pay) y anularlo (también AAL2: el cliente del API la pide solo).
import { useEffect, useMemo, useState } from 'react'
import { Can } from '../../kernel/access'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ClientPicker, ConfirmDialog, type DataColumn, DataTable, DateField, Modal, Panel, QBox, Spinner, toast } from '../../kernel/ui'
import { IconCash } from '../../kernel/ui/screenIcons'
import { PAYMENT_METHODS, todayIso, toPaymentRequest, voidPaymentError } from './billingRules'
import {
  consignmentPageReader,
  useConsignmentByClient,
  useConsignmentInvoice,
  useConsignmentInvoices,
  useConsignmentMutations,
  type BalanceFilter,
  type ConsignmentClientRowDto,
  type ConsignmentInvoiceListItemDto,
  type ConsignmentLineDto,
  type ConsignmentPaymentDto,
} from './consignmentApi'
import { BALANCE_FILTERS, consignmentCanPay, consignmentPayError } from './consignmentRules'

type View = 'invoices' | 'byClient'

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

/** Título del error del servidor más lo que dice por campo (sin repetir). */
function problemText(e: unknown): string {
  const p = applyProblemDetails(e)
  const extra = Object.values(p.errors).flat().filter((m) => m && m !== p.title)
  return [p.title, ...extra].join(' ')
}

export function ConsignmentTab() {
  const t = useT()
  const f = useFormat()
  const [view, setView] = useState<View>('invoices')
  const [balance, setBalance] = useState<BalanceFilter>('OPEN')
  const [clientId, setClientId] = useState<string | null>(null)
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [detail, setDetail] = useState<string | null>(null)
  const [paying, setPaying] = useState<{ invoice?: ConsignmentInvoiceListItemDto } | null>(null)
  const search = useDebounced(q.trim())
  const list = useConsignmentInvoices(balance, clientId, search, (page - 1) * pageSize, pageSize, view === 'invoices')
  const byClient = useConsignmentByClient()

  const columns = useMemo<DataColumn<ConsignmentInvoiceListItemDto>[]>(
    () => [
      { id: 'number', header: t('billing.col.number'), cell: (i) => <b>{i.number}</b>, exportValue: (i) => i.number ?? '', sortValue: (i) => i.number ?? '', card: 'title' },
      { id: 'client', header: t('billing.col.client'), cell: (i) => i.clientName, exportValue: (i) => i.clientName ?? '', sortValue: (i) => i.clientName ?? '' },
      { id: 'orders', header: t('billing.consignment.col.orders'), cell: (i) => (i.orderNumbers ?? []).join(', ') || '—', exportValue: (i) => (i.orderNumbers ?? []).join(', '), sortValue: (i) => (i.orderNumbers ?? []).join(',') },
      { id: 'issued', header: t('billing.col.issued'), cell: (i) => (i.issueDate ? f.date(i.issueDate) : '—'), exportValue: (i) => i.issueDate ?? '', sortValue: (i) => i.issueDate ?? '' },
      { id: 'consigned', header: t('billing.consignment.col.consigned'), cell: (i) => f.money(i.consigned ?? 0), exportValue: (i) => i.consigned ?? 0, sortValue: (i) => i.consigned ?? 0, align: 'end' },
      { id: 'returned', header: t('billing.consignment.col.returned'), cell: (i) => f.money(i.returned ?? 0), exportValue: (i) => i.returned ?? 0, sortValue: (i) => i.returned ?? 0, align: 'end' },
      { id: 'paid', header: t('billing.consignment.col.paid'), cell: (i) => f.money(i.paid ?? 0), exportValue: (i) => i.paid ?? 0, sortValue: (i) => i.paid ?? 0, align: 'end' },
      { id: 'balance', header: t('billing.col.balance'), cell: (i) => <b>{f.money(i.balance ?? 0)}</b>, exportValue: (i) => i.balance ?? 0, sortValue: (i) => i.balance ?? 0, align: 'end' },
      { id: 'status', header: t('billing.col.status'), cell: (i) => <Chip color={i.statusColor ?? undefined}>{i.statusLabel}</Chip>, exportValue: (i) => i.statusLabel ?? '', sortValue: (i) => i.statusLabel ?? '' },
    ],
    [t, f],
  )

  const clientColumns = useMemo<DataColumn<ConsignmentClientRowDto>[]>(
    () => [
      { id: 'client', header: t('billing.col.client'), cell: (r) => <b>{r.clientName}</b>, exportValue: (r) => r.clientName ?? '', sortValue: (r) => r.clientName ?? '', card: 'title' },
      { id: 'open', header: t('billing.consignment.col.openInvoices'), cell: (r) => `${r.openInvoiceCount ?? 0} / ${r.invoiceCount ?? 0}`, exportValue: (r) => r.openInvoiceCount ?? 0, sortValue: (r) => r.openInvoiceCount ?? 0, align: 'end' },
      { id: 'oldest', header: t('billing.consignment.col.oldest'), cell: (r) => (r.oldestOpenIssueDate ? f.date(r.oldestOpenIssueDate) : '—'), exportValue: (r) => r.oldestOpenIssueDate ?? '', sortValue: (r) => r.oldestOpenIssueDate ?? '' },
      { id: 'consigned', header: t('billing.consignment.col.consigned'), cell: (r) => f.money(r.consigned ?? 0), exportValue: (r) => r.consigned ?? 0, sortValue: (r) => r.consigned ?? 0, align: 'end' },
      { id: 'returned', header: t('billing.consignment.col.returned'), cell: (r) => f.money(r.returned ?? 0), exportValue: (r) => r.returned ?? 0, sortValue: (r) => r.returned ?? 0, align: 'end' },
      { id: 'paid', header: t('billing.consignment.col.paid'), cell: (r) => f.money(r.paid ?? 0), exportValue: (r) => r.paid ?? 0, sortValue: (r) => r.paid ?? 0, align: 'end' },
      { id: 'balance', header: t('billing.col.balance'), cell: (r) => <b>{f.money(r.balance ?? 0)}</b>, exportValue: (r) => r.balance ?? 0, sortValue: (r) => r.balance ?? 0, align: 'end' },
    ],
    [t, f],
  )

  const reset = () => setPage(1)
  const openClient = (id: string) => {
    setClientId(id)
    setBalance('OPEN')
    setView('invoices')
    reset()
  }

  return (
    <>
      <Panel
        flush
        icon={<IconCash />}
        title={t('billing.consignment.panel')}
        badge={view === 'invoices' ? (list.isPending ? undefined : list.data?.total) : byClient.isPending ? undefined : (byClient.data?.items ?? []).length}
      >
        <div className="pb">
          <p className="help">{t('billing.consignment.help')}</p>
          <div className="qrow tax-filters">
            <div className="seg" role="group" aria-label={t('billing.consignment.view')}>
              <button type="button" className={view === 'invoices' ? 'on' : ''} aria-pressed={view === 'invoices'} onClick={() => setView('invoices')}>{t('billing.consignment.viewInvoices')}</button>
              <button type="button" className={view === 'byClient' ? 'on' : ''} aria-pressed={view === 'byClient'} onClick={() => setView('byClient')}>{t('billing.consignment.viewByClient')}</button>
            </div>
            {view === 'invoices' && (
              <>
                <QBox value={q} onChange={(v) => (setQ(v), reset())} placeholder={t('billing.consignment.search')} />
                <select aria-label={t('billing.consignment.filterBalance')} value={balance} onChange={(e) => (setBalance(e.target.value as BalanceFilter), reset())}>
                  {BALANCE_FILTERS.map((b) => (
                    <option key={b} value={b}>{t(`billing.consignment.balance.${b}`)}</option>
                  ))}
                </select>
                <ClientPicker aria-label={t('billing.consignment.filterClient')} placeholder={t('billing.allClients')} value={clientId} onChange={(id) => (setClientId(id), reset())} />
              </>
            )}
            <Can perm="billing.consignment.pay">
              <button type="button" className="btn flow" onClick={() => setPaying({})}>{t('billing.consignment.record')}</button>
            </Can>
          </div>
          {view === 'invoices' ? (
            list.isError ? (
              <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
            ) : list.isPending ? (
              <Spinner block />
            ) : (
              <DataTable
                label={t('billing.consignment.panel')}
                columns={columns}
                rows={list.data?.items ?? []}
                rowKey={(i) => i.publicId ?? ''}
                page={page}
                pageSize={pageSize}
                total={list.data?.total ?? 0}
                onPage={setPage}
                onPageSize={(s) => (setPageSize(s), setPage(1))}
                onRowClick={(i) => setDetail(i.publicId!)}
                empty={t('billing.consignment.empty')}
                exportRows={() => fetchAllPages<ConsignmentInvoiceListItemDto>(consignmentPageReader(balance, clientId, search))}
                rowActions={[{ key: 'pay', label: t('billing.consignment.record'), onClick: (i) => setPaying({ invoice: i }), perm: 'billing.consignment.pay', visible: (i) => consignmentCanPay(i.balance) }]}
              />
            )
          ) : byClient.isError ? (
            <p className="ferr" role="alert">{applyProblemDetails(byClient.error).title}</p>
          ) : byClient.isPending ? (
            <Spinner block />
          ) : (
            <>
              <DataTable
                label={t('billing.consignment.viewByClient')}
                columns={clientColumns}
                rows={byClient.data?.items ?? []}
                rowKey={(r) => r.clientPublicId ?? ''}
                onRowClick={(r) => openClient(r.clientPublicId!)}
                empty={t('billing.consignment.emptyClients')}
                pageSize={25}
              />
              <p className="sales-totals" role="status">
                {t('billing.consignment.totals', {
                  consigned: f.money(byClient.data?.consigned ?? 0),
                  returned: f.money(byClient.data?.returned ?? 0),
                  paid: f.money(byClient.data?.paid ?? 0),
                  balance: f.money(byClient.data?.balance ?? 0),
                })}
              </p>
              <p className="help">{t('billing.consignment.byClientHelp')}</p>
            </>
          )}
        </div>
      </Panel>
      {detail !== null && <ConsignmentDetailModal key={detail} publicId={detail} onClose={() => setDetail(null)} onPay={(i) => setPaying({ invoice: i })} />}
      {paying && <ConsignmentPayDialog key={paying.invoice?.publicId ?? 'new'} preset={paying.invoice} onClose={() => setPaying(null)} />}
    </>
  )
}

// ---------------------------------------------------------------- ficha de la factura en consignación

function ConsignmentDetailModal({ publicId, onClose, onPay }: { publicId: string; onClose: () => void; onPay: (invoice: ConsignmentInvoiceListItemDto) => void }) {
  const t = useT()
  const f = useFormat()
  const detail = useConsignmentInvoice(publicId)
  const [voiding, setVoiding] = useState<ConsignmentPaymentDto | null>(null)
  const data = detail.data
  const inv = data?.invoice

  const lineColumns = useMemo<DataColumn<ConsignmentLineDto>[]>(
    () =>
      [
        { id: 'order', header: t('billing.consignment.col.order'), cell: (l) => l.orderNumber ?? '—', sortValue: (l) => l.orderNumber ?? '' },
        { id: 'desc', header: t('billing.col.description'), cell: (l) => l.description, sortValue: (l) => l.description ?? '', card: 'title' },
        { id: 'qty', header: t('billing.col.qty'), cell: (l) => f.number(l.quantity ?? 0), sortValue: (l) => l.quantity ?? 0, align: 'end' },
        { id: 'amount', header: t('billing.consignment.col.consigned'), cell: (l) => f.money(l.amount ?? 0), sortValue: (l) => l.amount ?? 0, align: 'end' },
        { id: 'returned', header: t('billing.consignment.col.returned'), cell: (l) => f.money(l.returned ?? 0), sortValue: (l) => l.returned ?? 0, align: 'end' },
      ],
    [t, f],
  )

  const payColumns = useMemo<DataColumn<ConsignmentPaymentDto>[]>(
    () => [
      { id: 'date', header: t('billing.pay.date'), cell: (p) => f.date(p.paidDate ?? ''), sortValue: (p) => p.paidDate ?? '' },
      { id: 'method', header: t('billing.pay.method'), cell: (p) => p.methodLabel ?? p.method, sortValue: (p) => p.method ?? '' },
      { id: 'ref', header: t('billing.pay.reference'), cell: (p) => p.reference ?? '—', sortValue: (p) => p.reference ?? '' },
      { id: 'amount', header: t('billing.pay.amount'), cell: (p) => (p.isVoided ? <s>{f.money(p.amount ?? 0)}</s> : f.money(p.amount ?? 0)), sortValue: (p) => p.amount ?? 0, align: 'end' },
      { id: 'by', header: t('billing.consignment.col.recordedBy'), cell: (p) => p.recordedByName ?? '—', sortValue: (p) => p.recordedByName ?? '' },
      { id: 'status', header: t('billing.col.status'), cell: (p) => (p.isVoided ? <Chip tone="warn" title={p.voidReason ?? undefined}>{t('billing.pay.voided')}</Chip> : '—') },
    ],
    [t, f],
  )

  return (
    <Modal
      open
      size="lg"
      title={t('billing.consignment.detailTitle', { number: inv?.number ?? '' })}
      onClose={onClose}
      footer={
        <>
          {inv && consignmentCanPay(inv.balance) && (
            <Can perm="billing.consignment.pay">
              <button type="button" className="btn flow" onClick={() => onPay(inv)}>{t('billing.consignment.record')}</button>
            </Can>
          )}
          <button type="button" className="btn" onClick={onClose}>{t('billing.close')}</button>
        </>
      }
    >
      <div className="pb">
        {detail.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(detail.error).title}</p>
        ) : !data || !inv ? (
          <Spinner block />
        ) : (
          <>
            <dl className="sales-meta">
              <div><dt>{t('billing.col.status')}</dt><dd><Chip color={inv.statusColor ?? undefined}>{inv.statusLabel}</Chip></dd></div>
              <div><dt>{t('billing.col.client')}</dt><dd>{inv.clientName}</dd></div>
              <div><dt>{t('billing.col.issued')}</dt><dd>{inv.issueDate ? f.date(inv.issueDate) : '—'}</dd></div>
              <div><dt>{t('billing.col.due')}</dt><dd>{inv.dueDate ? f.date(inv.dueDate) : '—'}</dd></div>
              <div><dt>{t('billing.consignment.col.orders')}</dt><dd>{(inv.orderNumbers ?? []).join(', ') || '—'}</dd></div>
            </dl>
            <p className="sales-totals" role="status">
              {t('billing.consignment.totals', { consigned: f.money(inv.consigned ?? 0), returned: f.money(inv.returned ?? 0), paid: f.money(inv.paid ?? 0), balance: f.money(inv.balance ?? 0) })}
            </p>
            <h3>{t('billing.consignment.lines')}</h3>
            <DataTable label={t('billing.consignment.lines')} columns={lineColumns} rows={data.lines ?? []} rowKey={(l) => String(l.invoiceLineId)} empty={t('billing.invoice.noLines')} />
            <h3>{t('billing.pay.title')}</h3>
            <DataTable
              label={t('billing.pay.title')}
              columns={payColumns}
              rows={data.payments ?? []}
              rowKey={(p) => p.publicId ?? ''}
              empty={t('billing.pay.empty')}
              rowActions={[{ key: 'void', label: t('billing.pay.void'), onClick: (p) => setVoiding(p), perm: 'billing.consignment.pay', visible: (p) => !p.isVoided }]}
            />
          </>
        )}
      </div>
      {voiding && <VoidConsignmentPaymentDialog payment={voiding} onClose={() => setVoiding(null)} />}
    </Modal>
  )
}

function VoidConsignmentPaymentDialog({ payment, onClose }: { payment: ConsignmentPaymentDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const { voidPayment } = useConsignmentMutations()
  const [reason, setReason] = useState('')
  const [problem, setProblem] = useState<string | null>(null)
  return (
    <ConfirmDialog
      open
      title={t('billing.pay.voidTitle', { amount: f.money(payment.amount ?? 0) })}
      message={
        <>
          <p>{t('billing.consignment.voidBody')}</p>
          <div className="f">
            <label htmlFor="void-cons-reason">{t('billing.pay.voidReason')}</label>
            <input id="void-cons-reason" value={reason} maxLength={250} autoComplete="off" onChange={(e) => setReason(e.target.value)} />
          </div>
          {problem && <p className="ferr" role="alert">{problem}</p>}
        </>
      }
      confirmLabel={t('billing.pay.void')}
      tone="danger"
      onConfirm={async () => {
        const bad = voidPaymentError(reason)
        if (bad) {
          const msg = t(`billing.pay.errors.void.${bad}`)
          setProblem(msg)
          throw new Error(msg)
        }
        setProblem(null)
        try {
          await voidPayment.mutateAsync({ paymentId: payment.publicId!, reason: reason.trim() })
        } catch (e) {
          setProblem(problemText(e))
          throw e
        }
        toast.success(t('billing.pay.voidedToast'))
      }}
      onClose={onClose}
    />
  )
}

// ---------------------------------------------------------------- registrar un pago

function ConsignmentPayDialog({ preset, onClose }: { preset?: ConsignmentInvoiceListItemDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const { pay } = useConsignmentMutations()
  const today = todayIso()
  const [clientId, setClientId] = useState<string | null>(preset?.clientPublicId ?? null)
  const [invoiceId, setInvoiceId] = useState<string | null>(preset?.publicId ?? null)
  const [form, setForm] = useState({ amount: preset ? String(preset.balance ?? '') : '', method: 'CHECK', paidDate: today, reference: '' })
  const [error, setError] = useState<string | null>(null)
  const set = (patch: Partial<typeof form>) => setForm((x) => ({ ...x, ...patch }))
  const open = useConsignmentInvoices('OPEN', clientId, '', 0, 200, clientId !== null)
  const options = (clientId ? open.data?.items : undefined) ?? []
  const chosen = options.find((i) => i.publicId === invoiceId) ?? (preset && preset.publicId === invoiceId ? preset : undefined)
  const balance = chosen?.balance ?? 0

  const pickInvoice = (id: string) => {
    setInvoiceId(id || null)
    const inv = options.find((i) => i.publicId === id)
    if (inv) set({ amount: String(inv.balance ?? '') })
  }

  const save = async () => {
    const problem = consignmentPayError(invoiceId, form, balance, today, chosen?.issueDate)
    if (problem) return setError(t(`billing.consignment.errors.${problem}`))
    setError(null)
    try {
      await pay.mutateAsync({ id: invoiceId!, body: toPaymentRequest(form, today) })
      toast.success(t('billing.pay.recorded'))
      onClose()
    } catch (e) {
      setError(problemText(e))
    }
  }

  return (
    <Modal
      open
      size="sm"
      title={t('billing.consignment.record')}
      onClose={onClose}
      dismissible={!pay.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={pay.isPending}>{t('billing.close')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={pay.isPending}>{pay.isPending ? t('common.loading') : t('billing.pay.save')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('billing.consignment.payHelp')}</p>
        <div className="f">
          <label htmlFor="cp-client">{t('billing.col.client')}</label>
          <ClientPicker id="cp-client" value={clientId} onChange={(id) => (setClientId(id), setInvoiceId(null), set({ amount: '' }))} />
        </div>
        <div className="f">
          <label htmlFor="cp-invoice">{t('billing.col.number')}</label>
          <select id="cp-invoice" value={invoiceId ?? ''} disabled={clientId === null} onChange={(e) => pickInvoice(e.target.value)}>
            <option value="">{open.isPending && clientId ? t('common.loading') : t('billing.consignment.chooseInvoice')}</option>
            {preset && !options.some((i) => i.publicId === preset.publicId) && <option value={preset.publicId}>{preset.number}</option>}
            {options.map((i) => (
              <option key={i.publicId} value={i.publicId}>{t('billing.consignment.invoiceOption', { number: i.number ?? '', balance: f.money(i.balance ?? 0) })}</option>
            ))}
          </select>
          {clientId !== null && !open.isPending && options.length === 0 && <p className="help">{t('billing.consignment.noOpenInvoices')}</p>}
        </div>
        {chosen && <p className="help">{t('billing.consignment.balanceHelp', { number: chosen.number ?? '', balance: f.money(balance) })}</p>}
        <div className="f">
          <label htmlFor="cp-amount">{t('billing.pay.amount')}</label>
          <input id="cp-amount" inputMode="decimal" value={form.amount} onChange={(e) => set({ amount: e.target.value })} />
        </div>
        <div className="f">
          <label htmlFor="cp-method">{t('billing.pay.method')}</label>
          <select id="cp-method" value={form.method} onChange={(e) => set({ method: e.target.value })}>
            {PAYMENT_METHODS.map((m) => (
              <option key={m} value={m}>{t(`billing.methods.${m}`)}</option>
            ))}
          </select>
        </div>
        <div className="f">
          <label htmlFor="cp-date">{t('billing.pay.date')}</label>
          <DateField id="cp-date" max={today} min={chosen?.issueDate ?? undefined} value={form.paidDate} onChange={(v) => set({ paidDate: v })} />
        </div>
        <div className="f">
          <label htmlFor="cp-ref">{t('billing.pay.reference')}</label>
          <input id="cp-ref" value={form.reference} maxLength={80} autoComplete="off" placeholder={t('billing.pay.referenceHint')} onChange={(e) => set({ reference: e.target.value })} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}
