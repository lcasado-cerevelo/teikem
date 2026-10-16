// Cobros (Lote 76): los PAGOS de una factura (registrar y anular), las CUENTAS POR COBRAR (por cliente, con antigüedad) y el ESTADO DE CUENTA de un cliente.
// Registrar un pago pide billing.generate; anularlo, billing.approve y reautenticación (AAL2: el cliente del API la pide solo).
import { useMemo, useState } from 'react'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, Modal, Panel, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { IconCash } from '../../kernel/ui/screenIcons'
import {
  useReceivables,
  useStatement,
  usePaymentMutations,
  type AgingDto,
  type InvoiceDto,
  type PaymentDto,
  type ReceivableRowDto,
  type StatementLineDto,
} from './billingApi'
import { AGING_KEYS, invoiceCanPay, PAYMENT_METHODS, paymentCanVoid, paymentFormError, statementFrom, todayIso, toPaymentRequest, voidPaymentError } from './billingRules'

// ---------------------------------------------------------------- pagos de una factura

export function PaymentsSection({ invoice }: { invoice: InvoiceDto }) {
  const t = useT()
  const f = useFormat()
  const [paying, setPaying] = useState(false)
  const [voiding, setVoiding] = useState<PaymentDto | null>(null)
  const payments = invoice.payments ?? []
  const canPay = invoiceCanPay(invoice.status)

  const columns = useMemo<DataColumn<PaymentDto>[]>(
    () => [
      { id: 'date', header: t('billing.pay.date'), cell: (p) => f.date(p.paidDate ?? ''), sortValue: (p) => p.paidDate ?? '', exportValue: (p) => p.paidDate ?? '' },
      { id: 'method', header: t('billing.pay.method'), cell: (p) => t(`billing.methods.${p.method}`), sortValue: (p) => p.method ?? '', exportValue: (p) => p.method ?? '' },
      { id: 'ref', header: t('billing.pay.reference'), cell: (p) => p.reference ?? '—', sortValue: (p) => p.reference ?? '', exportValue: (p) => p.reference ?? '' },
      {
        id: 'amount',
        header: t('billing.pay.amount'),
        cell: (p) => (p.voidedAtUtc ? <s>{f.money(p.amount ?? 0)}</s> : f.money(p.amount ?? 0)),
        sortValue: (p) => p.amount ?? 0,
        exportValue: (p) => p.amount ?? 0,
        align: 'end',
      },
      {
        id: 'status',
        header: t('billing.col.status'),
        cell: (p) => (p.voidedAtUtc ? <Chip tone="warn" title={p.voidReason ?? undefined}>{t('billing.pay.voided')}</Chip> : '—'),
        exportValue: (p) => (p.voidedAtUtc ? p.voidReason ?? '' : ''),
      },
    ],
    [t, f],
  )

  return (
    <div className="pay-section">
      <div className="pay-head">
        <h3>{t('billing.pay.title')}</h3>
        {canPay && (
          <Can perm="billing.generate">
            <button type="button" className="btn flow" onClick={() => setPaying(true)}>{t('billing.pay.record')}</button>
          </Can>
        )}
      </div>
      <DataTable
        label={t('billing.pay.title')}
        columns={columns}
        rows={payments}
        rowKey={(p) => String(p.id)}
        empty={t('billing.pay.empty')}
        rowActions={[{ key: 'void', label: t('billing.pay.void'), onClick: (p) => setVoiding(p), perm: 'billing.approve', visible: (p) => paymentCanVoid(invoice.status, p.voidedAtUtc) }]}
      />
      {paying && <PaymentDialog invoice={invoice} onClose={() => setPaying(false)} />}
      {voiding && <VoidPaymentDialog payment={voiding} onClose={() => setVoiding(null)} />}
    </div>
  )
}

function PaymentDialog({ invoice, onClose }: { invoice: InvoiceDto; onClose: () => void }) {
  const t = useT()
  const { record } = usePaymentMutations()
  const today = todayIso()
  const balance = invoice.balance ?? 0
  const [form, setForm] = useState({ amount: String(balance), method: 'CHECK', paidDate: today, reference: '' })
  const [error, setError] = useState<string | null>(null)
  const set = (patch: Partial<typeof form>) => setForm((x) => ({ ...x, ...patch }))

  const save = async () => {
    const problem = paymentFormError(form, balance, today, invoice.issueDate)
    if (problem) return setError(t(`billing.pay.errors.${problem}`))
    setError(null)
    try {
      await record.mutateAsync({ id: invoice.publicId!, body: toPaymentRequest(form, today) })
      toast.success(t('billing.pay.recorded'))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="sm"
      title={t('billing.pay.record')}
      onClose={onClose}
      dismissible={!record.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={record.isPending}>{t('billing.close')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={record.isPending}>{record.isPending ? t('common.loading') : t('billing.pay.save')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('billing.pay.balanceHelp', { number: invoice.number ?? '', balance: String(balance) })}</p>
        <div className="f">
          <label htmlFor="pay-amount">{t('billing.pay.amount')}</label>
          <input id="pay-amount" inputMode="decimal" value={form.amount} onChange={(e) => set({ amount: e.target.value })} />
        </div>
        <div className="f">
          <label htmlFor="pay-method">{t('billing.pay.method')}</label>
          <select id="pay-method" value={form.method} onChange={(e) => set({ method: e.target.value })}>
            {PAYMENT_METHODS.map((m) => (
              <option key={m} value={m}>{t(`billing.methods.${m}`)}</option>
            ))}
          </select>
        </div>
        <div className="f">
          <label htmlFor="pay-date">{t('billing.pay.date')}</label>
          <input id="pay-date" type="date" max={today} min={invoice.issueDate ?? undefined} value={form.paidDate} onChange={(e) => set({ paidDate: e.target.value })} />
        </div>
        <div className="f">
          <label htmlFor="pay-ref">{t('billing.pay.reference')}</label>
          <input id="pay-ref" value={form.reference} maxLength={80} autoComplete="off" placeholder={t('billing.pay.referenceHint')} onChange={(e) => set({ reference: e.target.value })} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

function VoidPaymentDialog({ payment, onClose }: { payment: PaymentDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const { voidPayment } = usePaymentMutations()
  const [reason, setReason] = useState('')
  const [problem, setProblem] = useState<string | null>(null)
  return (
    <ConfirmDialog
      open
      title={t('billing.pay.voidTitle', { amount: f.money(payment.amount ?? 0) })}
      message={
        <>
          <p>{t('billing.pay.voidBody')}</p>
          <div className="f">
            <label htmlFor="void-pay-reason">{t('billing.pay.voidReason')}</label>
            <input id="void-pay-reason" value={reason} maxLength={250} autoComplete="off" onChange={(e) => setReason(e.target.value)} />
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
        await voidPayment.mutateAsync({ paymentId: payment.id!, reason: reason.trim() })
        toast.success(t('billing.pay.voidedToast'))
      }}
      onClose={onClose}
    />
  )
}

// ---------------------------------------------------------------- cuentas por cobrar

function agingCell(f: ReturnType<typeof useFormat>, value: number | undefined) {
  return (value ?? 0) === 0 ? '—' : f.money(value ?? 0)
}

export function ReceivablesTab({ onOpenStatement }: { onOpenStatement: (clientId: string, clientName: string) => void }) {
  const t = useT()
  const f = useFormat()
  const list = useReceivables()
  const columns = useMemo<DataColumn<ReceivableRowDto>[]>(
    () => [
      { id: 'client', header: t('billing.col.client'), cell: (r) => <b>{r.clientName}</b>, exportValue: (r) => r.clientName ?? '', sortValue: (r) => r.clientName ?? '', card: 'title' },
      { id: 'open', header: t('billing.rec.open'), cell: (r) => f.number(r.openInvoices ?? 0), exportValue: (r) => r.openInvoices ?? 0, sortValue: (r) => r.openInvoices ?? 0, align: 'end' },
      ...AGING_KEYS.map(
        (k): DataColumn<ReceivableRowDto> => ({
          id: k,
          header: t(`billing.aging.${k}`),
          cell: (r) => agingCell(f, r.aging?.[k]),
          exportValue: (r) => r.aging?.[k] ?? 0,
          sortValue: (r) => r.aging?.[k] ?? 0,
          align: 'end',
        }),
      ),
      { id: 'balance', header: t('billing.col.balance'), cell: (r) => <b>{f.money(r.balance ?? 0)}</b>, exportValue: (r) => r.balance ?? 0, sortValue: (r) => r.balance ?? 0, align: 'end' },
      { id: 'oldest', header: t('billing.rec.oldest'), cell: (r) => (r.oldestDueDate ? f.date(r.oldestDueDate) : '—'), exportValue: (r) => r.oldestDueDate ?? '', sortValue: (r) => r.oldestDueDate ?? '' },
    ],
    [t, f],
  )
  const total = list.data?.total

  return (
    <Panel flush icon={<IconCash />} title={t('billing.rec.panel')} badge={list.isPending ? undefined : list.data?.rows?.length}>
      <div className="pb">
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <>
            <DataTable
              label={t('billing.rec.panel')}
              columns={columns}
              rows={list.data?.rows ?? []}
              rowKey={(r) => r.clientPublicId ?? ''}
              onRowClick={(r) => onOpenStatement(r.clientPublicId!, r.clientName ?? '')}
              empty={t('billing.rec.empty')}
              pageSize={25}
            />
            {total && (total.total ?? 0) > 0 && <p className="sales-totals" role="status">{t('billing.rec.total', { total: f.money(total.total ?? 0), over: f.money((total.days1To30 ?? 0) + (total.days31To60 ?? 0) + (total.days61To90 ?? 0) + (total.over90 ?? 0)) })}</p>}
            <p className="help">{t('billing.rec.help')}</p>
          </>
        )}
      </div>
    </Panel>
  )
}

// ---------------------------------------------------------------- estado de cuenta

function AgingStrip({ aging }: { aging: AgingDto }) {
  const t = useT()
  const f = useFormat()
  return (
    <dl className="sales-meta" aria-label={t('billing.stmt.aging')}>
      {AGING_KEYS.map((k) => (
        <div key={k}>
          <dt>{t(`billing.aging.${k}`)}</dt>
          <dd>{f.money(aging[k] ?? 0)}</dd>
        </div>
      ))}
    </dl>
  )
}

export function StatementModal({ clientId, clientName, onClose, onOpenInvoice }: { clientId: string; clientName: string; onClose: () => void; onOpenInvoice: (publicId: string) => void }) {
  const t = useT()
  const f = useFormat()
  const [to, setTo] = useState(() => todayIso())
  const [from, setFrom] = useState(() => statementFrom(todayIso()))
  const stmt = useStatement(clientId, from, to)
  const data = stmt.data

  const columns = useMemo<DataColumn<StatementLineDto>[]>(
    () => [
      { id: 'date', header: t('billing.pay.date'), cell: (l) => f.date(l.date ?? ''), sortValue: (l) => l.date ?? '', exportValue: (l) => l.date ?? '' },
      { id: 'desc', header: t('billing.col.description'), cell: (l) => l.description, exportValue: (l) => l.description ?? '', sortValue: (l) => l.description ?? '', card: 'title' },
      { id: 'debit', header: t('billing.stmt.debit'), cell: (l) => ((l.debit ?? 0) === 0 ? '' : f.money(l.debit ?? 0)), sortValue: (l) => l.debit ?? 0, exportValue: (l) => l.debit ?? 0, align: 'end' },
      { id: 'credit', header: t('billing.stmt.credit'), cell: (l) => ((l.credit ?? 0) === 0 ? '' : f.money(l.credit ?? 0)), sortValue: (l) => l.credit ?? 0, exportValue: (l) => l.credit ?? 0, align: 'end' },
      { id: 'balance', header: t('billing.col.balance'), cell: (l) => <b>{f.money(l.balance ?? 0)}</b>, sortValue: (l) => l.balance ?? 0, exportValue: (l) => l.balance ?? 0, align: 'end' },
    ],
    [t, f],
  )

  return (
    <Modal open size="lg" title={t('billing.stmt.title', { client: clientName })} onClose={onClose} footer={<button type="button" className="btn" onClick={onClose}>{t('billing.close')}</button>}>
      <div className="pb">
        <div className="qrow tax-filters">
          <div className="f">
            <label htmlFor="st-from">{t('billing.new.from')}</label>
            <input id="st-from" type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} />
          </div>
          <div className="f">
            <label htmlFor="st-to">{t('billing.new.to')}</label>
            <input id="st-to" type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} />
          </div>
        </div>
        {stmt.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(stmt.error).title}</p>
        ) : !data ? (
          <Spinner block />
        ) : (
          <>
            <dl className="sales-meta">
              <div><dt>{t('billing.stmt.opening')}</dt><dd>{f.money(data.openingBalance ?? 0)}</dd></div>
              <div><dt>{t('billing.stmt.invoiced')}</dt><dd>{f.money(data.totalInvoiced ?? 0)}</dd></div>
              <div><dt>{t('billing.stmt.paid')}</dt><dd>{f.money(data.totalPaid ?? 0)}</dd></div>
              <div><dt>{t('billing.stmt.closing')}</dt><dd><b>{f.money(data.closingBalance ?? 0)}</b></dd></div>
            </dl>
            <DataTable
              label={t('billing.stmt.lines')}
              columns={columns}
              rows={data.lines ?? []}
              rowKey={(l) => `${l.kind}-${l.date}-${l.reference}-${l.balance}-${l.credit}`}
              onRowClick={(l) => l.invoicePublicId && onOpenInvoice(l.invoicePublicId)}
              empty={t('billing.stmt.empty')}
              pageSize={25}
            />
            <h3>{t('billing.stmt.aging')}</h3>
            {data.aging && <AgingStrip aging={data.aging} />}
          </>
        )}
      </div>
    </Modal>
  )
}
