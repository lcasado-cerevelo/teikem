// Contabilidad → Facturación (Lote 73, módulo BILLING, permiso billing.view): las CORRIDAS (generar → revisar → aprobar → cancelar), las FACTURAS (ficha, ajustar el monto de una línea con motivo, anular)
// y los AJUSTES (base de facturación del producto y días de vencimiento). Aprobar y anular exigen reautenticación (AAL2): el cliente del API la pide solo.
import { useEffect, useMemo, useState } from 'react'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ClientPicker, ConfirmDialog, DataTable, Modal, Panel, QBox, Spinner, Tabs, toast, type DataColumn } from '../../kernel/ui'
import { IconCash } from '../../kernel/ui/screenIcons'
import {
  invoicePageReader,
  useBillingSettings,
  useInvoice,
  useInvoiceMutations,
  useInvoices,
  useRun,
  useRunMutations,
  useRuns,
  useUpdateBillingSettings,
  type BillingRunListItemDto,
  type InvoiceLineDto,
  type CreditNoteDto,
  type InvoiceListItemDto,
} from './billingApi'
import { adjustError, INVOICE_STATUSES, invoiceCanCredit, invoiceCanVoid, monthStartIso, parseAmount, RUN_STATUSES, runFormError, runIsOpen, todayIso, toRunRequest, type RunForm } from './billingRules'
import { PaymentsSection, ReceivablesTab, StatementModal } from './CollectionsParts'
import { CreditEditor, CreditNoteModal, CreditNotesTab } from './CreditParts'
import { ExportSection } from './ExportParts'
import './billing.css'
import '../tax/tax.css'

type TabKey = 'runs' | 'invoices' | 'collections' | 'credits' | 'settings'

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

// ---------------------------------------------------------------- corridas

function RunsTab({ onOpen }: { onOpen: (id: number) => void }) {
  const t = useT()
  const f = useFormat()
  const [status, setStatus] = useState('')
  const list = useRuns(status)
  const columns = useMemo<DataColumn<BillingRunListItemDto>[]>(
    () => [
      { id: 'id', header: t('billing.col.run'), cell: (r) => <b>#{r.id}</b>, exportValue: (r) => r.id ?? 0, sortValue: (r) => r.id ?? 0, card: 'title' },
      { id: 'period', header: t('billing.col.period'), cell: (r) => `${f.date(r.periodStart)} – ${f.date(r.periodEnd)}`, exportValue: (r) => `${r.periodStart} – ${r.periodEnd}`, sortValue: (r) => r.periodStart ?? '' },
      { id: 'client', header: t('billing.col.client'), cell: (r) => r.clientName ?? t('billing.allClients'), sortValue: (r) => r.clientName ?? '' },
      { id: 'status', header: t('billing.col.status'), cell: (r) => <Chip color={r.statusColor ?? undefined}>{r.statusLabel}</Chip>, exportValue: (r) => r.statusLabel ?? '', sortValue: (r) => r.statusLabel ?? '' },
      { id: 'invoices', header: t('billing.col.invoices'), cell: (r) => r.invoiceCount, sortValue: (r) => r.invoiceCount ?? 0, align: 'end' },
      { id: 'total', header: t('billing.col.total'), cell: (r) => f.money(r.totalAmount ?? 0), exportValue: (r) => r.totalAmount ?? 0, sortValue: (r) => r.totalAmount ?? 0, align: 'end' },
      { id: 'generated', header: t('billing.col.generated'), cell: (r) => f.dateTime(r.generatedAtUtc), exportValue: (r) => r.generatedAtUtc ?? '', sortValue: (r) => r.generatedAtUtc ?? '' },
    ],
    [t, f],
  )
  return (
    <Panel flush icon={<IconCash />} title={t('billing.runs.panel')} badge={list.isPending ? undefined : (list.data ?? []).length}>
      <div className="pb">
        <div className="qrow tax-filters">
          <select aria-label={t('billing.filterStatus')} value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="">{t('billing.allStatuses')}</option>
            {RUN_STATUSES.map((s) => (
              <option key={s} value={s}>{t(`billing.runStatuses.${s}`)}</option>
            ))}
          </select>
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable label={t('billing.runs.panel')} columns={columns} rows={list.data ?? []} rowKey={(r) => String(r.id)} onRowClick={(r) => onOpen(r.id!)} empty={t('billing.runs.empty')} pageSize={25} />
        )}
      </div>
    </Panel>
  )
}

function NewRunModal({ onClose, onCreated }: { onClose: () => void; onCreated: (id: number) => void }) {
  const t = useT()
  const { generate } = useRunMutations()
  const [form, setForm] = useState<RunForm>(() => ({ clientPublicId: null, periodStart: monthStartIso(), periodEnd: todayIso(), onlyInPeriod: false, includeProducts: true, includeFreight: true, note: '' }))
  const [error, setError] = useState<string | null>(null)
  const set = (patch: Partial<RunForm>) => setForm((x) => ({ ...x, ...patch }))

  const submit = async () => {
    const problem = runFormError(form)
    if (problem) return setError(t(`billing.errors.${problem}`))
    setError(null)
    try {
      const run = await generate.mutateAsync(toRunRequest(form))
      toast.success(t('billing.generated', { id: run.id ?? 0, count: run.invoiceCount ?? 0 }))
      onCreated(run.id!)
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('billing.new.title')}
      onClose={onClose}
      dismissible={!generate.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={generate.isPending}>{t('billing.close')}</button>
          <button type="button" className="btn flow" onClick={() => void submit()} disabled={generate.isPending}>{generate.isPending ? t('common.loading') : t('billing.new.generate')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('billing.new.help')}</p>
        <div className="f">
          <label htmlFor="br-client">{t('billing.new.client')}</label>
          <ClientPicker id="br-client" value={form.clientPublicId} onChange={(id) => set({ clientPublicId: id })} />
          <p className="help">{t('billing.new.clientHelp')}</p>
        </div>
        <div className="r2">
          <div className="f">
            <label htmlFor="br-from">{t('billing.new.from')}</label>
            <input id="br-from" type="date" value={form.periodStart} onChange={(e) => set({ periodStart: e.target.value })} />
          </div>
          <div className="f">
            <label htmlFor="br-to">{t('billing.new.to')}</label>
            <input id="br-to" type="date" value={form.periodEnd} onChange={(e) => set({ periodEnd: e.target.value })} />
          </div>
        </div>
        <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <input type="checkbox" checked={form.includeProducts} onChange={(e) => set({ includeProducts: e.target.checked })} />
          {t('billing.new.includeProducts')}
        </label>
        <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <input type="checkbox" checked={form.includeFreight} onChange={(e) => set({ includeFreight: e.target.checked })} />
          {t('billing.new.includeFreight')}
        </label>
        <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <input type="checkbox" checked={form.onlyInPeriod} onChange={(e) => set({ onlyInPeriod: e.target.checked })} />
          {t('billing.new.onlyInPeriod')}
        </label>
        <div className="f">
          <label htmlFor="br-note">{t('billing.new.note')}</label>
          <input id="br-note" value={form.note} maxLength={500} autoComplete="off" onChange={(e) => set({ note: e.target.value })} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

function RunModal({ id, onClose, onOpenInvoice }: { id: number; onClose: () => void; onOpenInvoice: (publicId: string) => void }) {
  const t = useT()
  const f = useFormat()
  const run = useRun(id)
  const { review, approve, cancel } = useRunMutations()
  const [cancelling, setCancelling] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const data = run.data
  const open = runIsOpen(data?.status)

  const act = async (p: Promise<unknown>, ok: string) => {
    setError(null)
    try {
      await p
      toast.success(ok)
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  const columns = useMemo<DataColumn<InvoiceListItemDto>[]>(
    () => [
      { id: 'number', header: t('billing.col.number'), cell: (i) => <b>{i.number}</b>, exportValue: (i) => i.number ?? '', sortValue: (i) => i.number ?? '', card: 'title' },
      { id: 'client', header: t('billing.col.client'), cell: (i) => i.clientName, sortValue: (i) => i.clientName ?? '' },
      { id: 'status', header: t('billing.col.status'), cell: (i) => <Chip color={i.statusColor ?? undefined}>{i.statusLabel}</Chip>, sortValue: (i) => i.statusLabel ?? '' },
      { id: 'subtotal', header: t('billing.col.subtotal'), cell: (i) => f.money(i.subtotal ?? 0), sortValue: (i) => i.subtotal ?? 0, align: 'end' },
      { id: 'tax', header: t('billing.col.tax'), cell: (i) => f.money(i.taxAmount ?? 0), sortValue: (i) => i.taxAmount ?? 0, align: 'end' },
      { id: 'total', header: t('billing.col.total'), cell: (i) => <b>{f.money(i.total ?? 0)}</b>, sortValue: (i) => i.total ?? 0, align: 'end' },
    ],
    [t, f],
  )

  return (
    <Modal
      open
      size="lg"
      title={t('billing.run.title', { id })}
      onClose={onClose}
      footer={
        <>
          {open && (
            <Can perm="billing.generate">
              <button type="button" className="btn danger" onClick={() => setCancelling(true)}>{t('billing.run.cancel')}</button>
            </Can>
          )}
          <button type="button" className="btn" onClick={onClose}>{t('billing.close')}</button>
          {data?.status === 'GENERATED' && (
            <Can perm="billing.generate">
              <button type="button" className="btn flow" disabled={review.isPending} onClick={() => void act(review.mutateAsync(id), t('billing.run.reviewed', { id }))}>{t('billing.run.review')}</button>
            </Can>
          )}
          {data?.status === 'REVIEWED' && (
            <Can perm="billing.approve">
              <button type="button" className="btn flow" disabled={approve.isPending} onClick={() => void act(approve.mutateAsync(id), t('billing.run.approved', { id }))}>{t('billing.run.approve')}</button>
            </Can>
          )}
        </>
      }
    >
      <div className="pb">
        {run.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(run.error).title}</p>
        ) : !data ? (
          <Spinner block />
        ) : (
          <>
            <dl className="sales-meta">
              <div><dt>{t('billing.col.status')}</dt><dd><Chip color={data.statusColor ?? undefined}>{data.statusLabel}</Chip></dd></div>
              <div><dt>{t('billing.col.period')}</dt><dd>{f.date(data.periodStart)} – {f.date(data.periodEnd)}</dd></div>
              <div><dt>{t('billing.col.client')}</dt><dd>{data.clientName ?? t('billing.allClients')}</dd></div>
              <div><dt>{t('billing.col.generated')}</dt><dd>{f.dateTime(data.generatedAtUtc)}</dd></div>
              {data.approvedAtUtc && <div><dt>{t('billing.run.approvedAt')}</dt><dd>{f.dateTime(data.approvedAtUtc)}</dd></div>}
              {data.note && <div><dt>{t('billing.new.note')}</dt><dd>{data.note}</dd></div>}
            </dl>
            <p className="help">{t(`billing.run.hint.${data.status}`)}</p>
            <DataTable label={t('billing.run.invoices')} columns={columns} rows={data.invoices ?? []} rowKey={(i) => i.publicId ?? ''} onRowClick={(i) => onOpenInvoice(i.publicId!)} empty={t('billing.run.noInvoices')} />
            <p className="sales-totals" role="status">{t('billing.run.total', { total: f.money(data.totalAmount ?? 0) })}</p>
            {error && <p className="ferr" role="alert">{error}</p>}
            <ExportSection run={data} />
          </>
        )}
      </div>
      <ConfirmDialog
        open={cancelling}
        title={t('billing.run.cancelTitle', { id })}
        message={t('billing.run.cancelBody')}
        confirmLabel={t('billing.run.cancel')}
        tone="danger"
        onConfirm={async () => {
          await cancel.mutateAsync({ id })
          toast.success(t('billing.run.cancelled', { id }))
          setCancelling(false)
        }}
        onClose={() => setCancelling(false)}
      />
    </Modal>
  )
}

// ---------------------------------------------------------------- facturas

function InvoicesTab({ onOpen }: { onOpen: (publicId: string) => void }) {
  const t = useT()
  const f = useFormat()
  const [status, setStatus] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const search = useDebounced(q.trim())
  const list = useInvoices(status, search, (page - 1) * pageSize, pageSize)
  const columns = useMemo<DataColumn<InvoiceListItemDto>[]>(
    () => [
      { id: 'number', header: t('billing.col.number'), cell: (i) => <b>{i.number}</b>, exportValue: (i) => i.number ?? '', sortValue: (i) => i.number ?? '', card: 'title' },
      { id: 'client', header: t('billing.col.client'), cell: (i) => i.clientName, sortValue: (i) => i.clientName ?? '' },
      { id: 'status', header: t('billing.col.status'), cell: (i) => <Chip color={i.statusColor ?? undefined}>{i.statusLabel}</Chip>, exportValue: (i) => i.statusLabel ?? '', sortValue: (i) => i.statusLabel ?? '' },
      { id: 'issued', header: t('billing.col.issued'), cell: (i) => (i.issueDate ? f.date(i.issueDate) : '—'), exportValue: (i) => i.issueDate ?? '', sortValue: (i) => i.issueDate ?? '' },
      { id: 'due', header: t('billing.col.due'), cell: (i) => (i.dueDate ? f.date(i.dueDate) : '—'), exportValue: (i) => i.dueDate ?? '', sortValue: (i) => i.dueDate ?? '' },
      { id: 'total', header: t('billing.col.total'), cell: (i) => <b>{f.money(i.total ?? 0)}</b>, exportValue: (i) => i.total ?? 0, sortValue: (i) => i.total ?? 0, align: 'end' },
      { id: 'balance', header: t('billing.col.balance'), cell: (i) => f.money(i.balance ?? 0), exportValue: (i) => i.balance ?? 0, sortValue: (i) => i.balance ?? 0, align: 'end' },
    ],
    [t, f],
  )
  return (
    <Panel flush icon={<IconCash />} title={t('billing.invoices.panel')} badge={list.isPending ? undefined : list.data?.total}>
      <div className="pb">
        <div className="qrow tax-filters">
          <QBox value={q} onChange={(v) => (setQ(v), setPage(1))} placeholder={t('billing.invoices.search')} />
          <select aria-label={t('billing.filterStatus')} value={status} onChange={(e) => (setStatus(e.target.value), setPage(1))}>
            <option value="">{t('billing.allStatuses')}</option>
            {INVOICE_STATUSES.map((s) => (
              <option key={s} value={s}>{t(`billing.invoiceStatuses.${s}`)}</option>
            ))}
          </select>
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('billing.invoices.panel')}
            columns={columns}
            rows={list.data?.items ?? []}
            rowKey={(i) => i.publicId ?? ''}
            page={page}
            pageSize={pageSize}
            total={list.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(s) => (setPageSize(s), setPage(1))}
            onRowClick={(i) => onOpen(i.publicId!)}
            empty={t('billing.invoices.empty')}
            exportRows={() => fetchAllPages<InvoiceListItemDto>(invoicePageReader(status, search))}
          />
        )}
      </div>
    </Panel>
  )
}

function AdjustDialog({ invoiceId, line, onClose }: { invoiceId: string; line: InvoiceLineDto; onClose: () => void }) {
  const t = useT()
  const { adjust } = useInvoiceMutations()
  const [amount, setAmount] = useState(String(line.unitAmount ?? ''))
  const [note, setNote] = useState(line.adjustmentNote ?? '')
  const [error, setError] = useState<string | null>(null)

  const save = async () => {
    const problem = adjustError(amount, note)
    if (problem) return setError(t(`billing.errors.adjust.${problem}`))
    setError(null)
    try {
      await adjust.mutateAsync({ id: invoiceId, lineId: line.id!, unitAmount: parseAmount(amount)!, note: note.trim() })
      toast.success(t('billing.adjusted'))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="sm"
      title={t('billing.adjust.title')}
      onClose={onClose}
      dismissible={!adjust.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={adjust.isPending}>{t('billing.close')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={adjust.isPending}>{adjust.isPending ? t('common.loading') : t('billing.adjust.save')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{line.description}</p>
        {line.originalUnitAmount != null && <p className="help">{t('billing.adjust.original', { amount: String(line.originalUnitAmount) })}</p>}
        <div className="f">
          <label htmlFor="adj-amount">{t('billing.adjust.amount')}</label>
          <input id="adj-amount" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </div>
        <div className="f">
          <label htmlFor="adj-note">{t('billing.adjust.note')}</label>
          <input id="adj-note" value={note} maxLength={250} autoComplete="off" onChange={(e) => setNote(e.target.value)} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

function InvoiceModal({ publicId, onClose, onCredit }: { publicId: string; onClose: () => void; onCredit: (publicId: string) => void }) {
  const t = useT()
  const f = useFormat()
  const invoice = useInvoice(publicId)
  const run = useRun(invoice.data?.runId ?? null)
  const { voidInvoice } = useInvoiceMutations()
  const canAdjust = useCan('billing.generate')
  const [adjusting, setAdjusting] = useState<InvoiceLineDto | null>(null)
  const [voiding, setVoiding] = useState(false)
  const [reason, setReason] = useState('')
  const data = invoice.data
  const editable = runIsOpen(run.data?.status)

  const columns = useMemo<DataColumn<InvoiceLineDto>[]>(
    () => [
      { id: 'desc', header: t('billing.col.description'), cell: (l) => (<>{l.description}{l.isAdjusted && <> <Chip tone="warn">{t('billing.adjustedChip')}</Chip></>}</>), exportValue: (l) => l.description ?? '', sortValue: (l) => l.description ?? '', card: 'title' },
      { id: 'qty', header: t('billing.col.qty'), cell: (l) => f.number(l.quantity ?? 0), sortValue: (l) => l.quantity ?? 0, align: 'end' },
      { id: 'unit', header: t('billing.col.unit'), cell: (l) => f.money(l.unitAmount ?? 0), sortValue: (l) => l.unitAmount ?? 0, align: 'end' },
      { id: 'sub', header: t('billing.col.subtotal'), cell: (l) => f.money(l.lineSubtotal ?? 0), sortValue: (l) => l.lineSubtotal ?? 0, align: 'end' },
      { id: 'tax', header: t('billing.col.tax'), cell: (l) => (l.isTaxExempt ? t('billing.exempt') : f.money(l.taxAmount ?? 0)), sortValue: (l) => l.taxAmount ?? 0, align: 'end' },
    ],
    [t, f],
  )

  return (
    <Modal
      open
      size="lg"
      title={t('billing.invoice.title', { number: data?.number ?? '' })}
      onClose={onClose}
      footer={
        <>
          {data && invoiceCanCredit(data.status) && (
            <Can perm="billing.credit">
              <button type="button" className="btn" onClick={() => onCredit(publicId)}>{t('billing.invoice.credit')}</button>
            </Can>
          )}
          {data && invoiceCanVoid(data.status) && (
            <Can perm="billing.approve">
              <button type="button" className="btn danger" onClick={() => setVoiding(true)}>{t('billing.invoice.void')}</button>
            </Can>
          )}
          <button type="button" className="btn" onClick={onClose}>{t('billing.close')}</button>
        </>
      }
    >
      <div className="pb">
        {invoice.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(invoice.error).title}</p>
        ) : !data ? (
          <Spinner block />
        ) : (
          <>
            <dl className="sales-meta">
              <div><dt>{t('billing.col.status')}</dt><dd><Chip color={data.statusColor ?? undefined}>{data.statusLabel}</Chip></dd></div>
              <div><dt>{t('billing.col.client')}</dt><dd>{data.clientName}</dd></div>
              <div><dt>{t('billing.col.issued')}</dt><dd>{data.issueDate ? f.date(data.issueDate) : '—'}</dd></div>
              <div><dt>{t('billing.col.due')}</dt><dd>{data.dueDate ? f.date(data.dueDate) : '—'}</dd></div>
              {data.runId != null && <div><dt>{t('billing.col.run')}</dt><dd>#{data.runId}</dd></div>}
            </dl>
            <DataTable
              label={t('billing.invoice.lines')}
              columns={columns}
              rows={data.lines ?? []}
              rowKey={(l) => String(l.id)}
              empty={t('billing.invoice.noLines')}
              rowActions={[{ key: 'adjust', label: t('billing.adjust.action'), onClick: (l) => setAdjusting(l), perm: 'billing.generate', visible: () => canAdjust && editable }]}
            />
            <p className="sales-totals" role="status">
              {t('billing.invoice.totals', { subtotal: f.money(data.subtotal ?? 0), tax: f.money(data.taxAmount ?? 0), total: f.money(data.total ?? 0) })}
              {(data.paid ?? 0) > 0 && ` · ${t('billing.invoice.paid', { paid: f.money(data.paid ?? 0), balance: f.money(data.balance ?? 0) })}`}
              {(data.credited ?? 0) > 0 && ` · ${t('billing.invoice.credited', { credited: f.money(data.credited ?? 0), balance: f.money(data.balance ?? 0) })}`}
            </p>
            {editable && canAdjust && <p className="help">{t('billing.invoice.adjustHelp')}</p>}
            <PaymentsSection invoice={data} />
          </>
        )}
      </div>
      {adjusting && data && <AdjustDialog invoiceId={data.publicId!} line={adjusting} onClose={() => setAdjusting(null)} />}
      <ConfirmDialog
        open={voiding}
        title={t('billing.invoice.voidTitle', { number: data?.number ?? '' })}
        message={
          <>
            <p>{t('billing.invoice.voidBody')}</p>
            <div className="f">
              <label htmlFor="void-reason">{t('billing.invoice.voidReason')}</label>
              <input id="void-reason" value={reason} maxLength={400} autoComplete="off" onChange={(e) => setReason(e.target.value)} />
            </div>
          </>
        }
        confirmLabel={t('billing.invoice.void')}
        tone="danger"
        onConfirm={async () => {
          await voidInvoice.mutateAsync({ id: publicId, comment: reason.trim() || undefined })
          toast.success(t('billing.invoice.voided', { number: data?.number ?? '' }))
          setVoiding(false)
          setReason('')
        }}
        onClose={() => setVoiding(false)}
      />
    </Modal>
  )
}

// ---------------------------------------------------------------- ajustes

function SettingsTab() {
  const t = useT()
  const settings = useBillingSettings()
  const update = useUpdateBillingSettings()
  const canEdit = useCan('billing.approve')
  const [trigger, setTrigger] = useState<string | null>(null)
  const [days, setDays] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const shownTrigger = trigger ?? settings.data?.salesBillingTrigger ?? 'ON_DISPATCH'
  const shownDays = days ?? String(settings.data?.invoiceDueDays ?? 30)

  const save = async () => {
    const n = Number(shownDays)
    if (!Number.isInteger(n) || n < 0 || n > 365) return setError(t('billing.settings.daysError'))
    setError(null)
    try {
      await update.mutateAsync({ salesBillingTrigger: shownTrigger, invoiceDueDays: n })
      setTrigger(null)
      setDays(null)
      toast.success(t('billing.settings.saved'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Panel icon={<IconCash />} title={t('billing.settings.panel')}>
      <div className="pb">
        {settings.isPending ? (
          <Spinner block />
        ) : (
          <>
            <div className="f">
              <label htmlFor="bs-trigger">{t('billing.settings.trigger')}</label>
              <select id="bs-trigger" value={shownTrigger} disabled={!canEdit} onChange={(e) => setTrigger(e.target.value)}>
                <option value="ON_DISPATCH">{t('billing.settings.onDispatch')}</option>
                <option value="ON_DELIVERY">{t('billing.settings.onDelivery')}</option>
              </select>
              <p className="help">{t('billing.settings.triggerHelp')}</p>
            </div>
            <div className="f">
              <label htmlFor="bs-days">{t('billing.settings.days')}</label>
              <input id="bs-days" inputMode="numeric" value={shownDays} disabled={!canEdit} onChange={(e) => setDays(e.target.value)} />
              <p className="help">{t('billing.settings.daysHelp')}</p>
            </div>
            {error && <p className="ferr" role="alert">{error}</p>}
            {canEdit ? (
              <button type="button" className="btn flow" onClick={() => void save()} disabled={update.isPending}>{update.isPending ? t('common.loading') : t('billing.settings.save')}</button>
            ) : (
              <p className="help">{t('billing.settings.readOnly')}</p>
            )}
          </>
        )}
      </div>
    </Panel>
  )
}

// ---------------------------------------------------------------- pantalla

export default function BillingScreen() {
  const t = useT()
  const [tab, setTab] = useState<TabKey>('runs')
  const [runId, setRunId] = useState<number | null>(null)
  const [newRun, setNewRun] = useState(false)
  const [invoiceId, setInvoiceId] = useState<string | null>(null)
  const [statement, setStatement] = useState<{ id: string; name: string } | null>(null)
  const [creditId, setCreditId] = useState<string | null>(null)
  const [creditEditor, setCreditEditor] = useState<{ invoicePublicId?: string | null; note?: CreditNoteDto | null } | null>(null)
  return (
    <div className="sales-screen">
      <div className="head">
        <div>
          <h1>{t('billing.title')}</h1>
          <p>{t('billing.subtitle')}</p>
        </div>
        <Can perm="billing.generate">
          <button type="button" className="btn flow" onClick={() => setNewRun(true)}>{t('billing.new.title')}</button>
        </Can>
      </div>
      <Tabs<TabKey>
        label={t('billing.tabs.label')}
        value={tab}
        onChange={setTab}
        tabs={[
          { key: 'runs', label: t('billing.tabs.runs') },
          { key: 'invoices', label: t('billing.tabs.invoices') },
          { key: 'collections', label: t('billing.tabs.collections') },
          { key: 'credits', label: t('billing.tabs.credits') },
          { key: 'settings', label: t('billing.tabs.settings') },
        ]}
      />
      {tab === 'runs' && <RunsTab onOpen={setRunId} />}
      {tab === 'invoices' && <InvoicesTab onOpen={setInvoiceId} />}
      {tab === 'collections' && <ReceivablesTab onOpenStatement={(id, name) => setStatement({ id, name })} />}
      {tab === 'credits' && <CreditNotesTab onOpen={setCreditId} onNew={() => setCreditEditor({})} />}
      {tab === 'settings' && <SettingsTab />}
      {newRun && (
        <NewRunModal
          onClose={() => setNewRun(false)}
          onCreated={(id) => {
            setNewRun(false)
            setRunId(id)
          }}
        />
      )}
      {runId !== null && <RunModal key={runId} id={runId} onClose={() => setRunId(null)} onOpenInvoice={setInvoiceId} />}
      {statement && <StatementModal key={statement.id} clientId={statement.id} clientName={statement.name} onClose={() => setStatement(null)} onOpenInvoice={setInvoiceId} />}
      {invoiceId !== null && <InvoiceModal key={invoiceId} publicId={invoiceId} onClose={() => setInvoiceId(null)} onCredit={(id) => setCreditEditor({ invoicePublicId: id })} />}
      {creditId !== null && (
        <CreditNoteModal key={creditId} id={creditId} onClose={() => setCreditId(null)} onEdit={(n) => setCreditEditor({ note: n })} onOpenInvoice={setInvoiceId} />
      )}
      {creditEditor && (
        <CreditEditor
          seed={creditEditor}
          onClose={() => setCreditEditor(null)}
          onSaved={(id) => {
            setCreditEditor(null)
            setInvoiceId(null)
            setTab('credits')
            setCreditId(id)
          }}
        />
      )}
    </div>
  )
}
