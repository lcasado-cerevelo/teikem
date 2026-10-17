// Notas de crédito (Lote 80): la lista, la ficha (aprobar, aplicar a facturas, anular) y el editor de una nota (sobre una factura o libre). Crear, cambiar, aplicar y anular piden billing.credit;
// aprobar pide además reautenticación (AAL2: el cliente del API la pide solo).
import { useEffect, useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { api, unwrap } from '../../kernel/api/client'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ClientPicker, ConfirmDialog, DataTable, Modal, Panel, QBox, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { IconCash } from '../../kernel/ui/screenIcons'
import {
  creditNotePageReader,
  useCreditMutations,
  useCreditNote,
  useCreditNotes,
  useInvoice,
  useInvoices,
  type CreditNoteDto,
  type CreditNoteListItemDto,
  type CreditNoteLineDto,
  type InvoiceDto,
  type InvoiceListItemDto,
} from './billingApi'
import {
  applyCreditError,
  blankCreditLine,
  CREDIT_CHARGE_TYPES,
  CREDIT_REASONS,
  CREDIT_STATUSES,
  creditFormError,
  defaultApplyAmount,
  invoiceCanCredit,
  lineValue,
  noteCanApply,
  noteCanApprove,
  noteCanEdit,
  noteCanVoid,
  parseAmount,
  toCreditRequest,
  voidPaymentError,
  type CreditForm,
  type CreditLineForm,
} from './billingRules'

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

// ---------------------------------------------------------------- lista

export function CreditNotesTab({ onOpen, onNew }: { onOpen: (id: string) => void; onNew: () => void }) {
  const t = useT()
  const f = useFormat()
  const [status, setStatus] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const search = useDebounced(q.trim())
  const list = useCreditNotes(status, search, (page - 1) * pageSize, pageSize)
  const columns = useMemo<DataColumn<CreditNoteListItemDto>[]>(
    () => [
      { id: 'number', header: t('billing.credit.number'), cell: (n) => <b>{n.number}</b>, exportValue: (n) => n.number ?? '', sortValue: (n) => n.number ?? '', card: 'title' },
      { id: 'client', header: t('billing.col.client'), cell: (n) => n.clientName, sortValue: (n) => n.clientName ?? '' },
      { id: 'invoice', header: t('billing.credit.invoice'), cell: (n) => n.invoiceNumber ?? '—', exportValue: (n) => n.invoiceNumber ?? '', sortValue: (n) => n.invoiceNumber ?? '' },
      { id: 'reason', header: t('billing.credit.reason'), cell: (n) => n.reasonLabel, exportValue: (n) => n.reasonLabel ?? '', sortValue: (n) => n.reasonLabel ?? '' },
      { id: 'status', header: t('billing.col.status'), cell: (n) => <Chip color={n.statusColor ?? undefined}>{n.statusLabel}</Chip>, exportValue: (n) => n.statusLabel ?? '', sortValue: (n) => n.statusLabel ?? '' },
      { id: 'total', header: t('billing.col.total'), cell: (n) => <b>{f.money(n.total ?? 0)}</b>, exportValue: (n) => n.total ?? 0, sortValue: (n) => n.total ?? 0, align: 'end' },
      { id: 'balance', header: t('billing.credit.balance'), cell: (n) => ((n.balance ?? 0) === 0 ? '—' : f.money(n.balance ?? 0)), exportValue: (n) => n.balance ?? 0, sortValue: (n) => n.balance ?? 0, align: 'end' },
      { id: 'created', header: t('billing.credit.created'), cell: (n) => f.date(n.createdAtUtc ?? ''), exportValue: (n) => n.createdAtUtc ?? '', sortValue: (n) => n.createdAtUtc ?? '' },
    ],
    [t, f],
  )
  return (
    <Panel
      flush
      icon={<IconCash />}
      title={t('billing.credit.panel')}
      badge={list.isPending ? undefined : list.data?.total}
      actions={
        <Can perm="billing.credit">
          <button type="button" className="btn flow" onClick={onNew}>{t('billing.credit.new')}</button>
        </Can>
      }
    >
      <div className="pb">
        <div className="qrow tax-filters">
          <QBox value={q} onChange={(v) => (setQ(v), setPage(1))} placeholder={t('billing.credit.search')} />
          <select aria-label={t('billing.filterStatus')} value={status} onChange={(e) => (setStatus(e.target.value), setPage(1))}>
            <option value="">{t('billing.allStatuses')}</option>
            {CREDIT_STATUSES.map((s) => (
              <option key={s} value={s}>{t(`billing.credit.statuses.${s}`)}</option>
            ))}
          </select>
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('billing.credit.panel')}
            columns={columns}
            rows={list.data?.items ?? []}
            rowKey={(n) => n.publicId ?? ''}
            page={page}
            pageSize={pageSize}
            total={list.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(s) => (setPageSize(s), setPage(1))}
            onRowClick={(n) => onOpen(n.publicId!)}
            empty={t('billing.credit.empty')}
            exportRows={() => fetchAllPages<CreditNoteListItemDto>(creditNotePageReader(status, search))}
          />
        )}
      </div>
    </Panel>
  )
}

// ---------------------------------------------------------------- ficha

export function CreditNoteModal({ id, onClose, onEdit, onOpenInvoice }: { id: string; onClose: () => void; onEdit: (note: CreditNoteDto) => void; onOpenInvoice: (publicId: string) => void }) {
  const t = useT()
  const f = useFormat()
  const note = useCreditNote(id)
  const { approve } = useCreditMutations()
  const [applying, setApplying] = useState(false)
  const [voiding, setVoiding] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const data = note.data

  const lineColumns = useMemo<DataColumn<CreditNoteLineDto>[]>(
    () => [
      { id: 'desc', header: t('billing.col.description'), cell: (l) => l.description, exportValue: (l) => l.description ?? '', sortValue: (l) => l.description ?? '', card: 'title' },
      { id: 'qty', header: t('billing.col.qty'), cell: (l) => f.number(l.quantity ?? 0), sortValue: (l) => l.quantity ?? 0, align: 'end' },
      { id: 'unit', header: t('billing.col.unit'), cell: (l) => f.money(l.unitAmount ?? 0), sortValue: (l) => l.unitAmount ?? 0, align: 'end' },
      { id: 'sub', header: t('billing.col.subtotal'), cell: (l) => f.money(l.lineSubtotal ?? 0), sortValue: (l) => l.lineSubtotal ?? 0, align: 'end' },
      { id: 'tax', header: t('billing.col.tax'), cell: (l) => (l.isTaxExempt ? t('billing.exempt') : f.money(l.taxAmount ?? 0)), sortValue: (l) => l.taxAmount ?? 0, align: 'end' },
    ],
    [t, f],
  )

  const doApprove = async () => {
    setError(null)
    try {
      await approve.mutateAsync(id)
      toast.success(t('billing.credit.approved', { number: data?.number ?? '' }))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="lg"
      title={t('billing.credit.title', { number: data?.number ?? '' })}
      onClose={onClose}
      footer={
        <>
          {data && noteCanVoid(data.status, data.applied) && (
            <Can perm="billing.credit">
              <button type="button" className="btn danger" onClick={() => setVoiding(true)}>{t('billing.credit.void')}</button>
            </Can>
          )}
          <button type="button" className="btn" onClick={onClose}>{t('billing.close')}</button>
          {data && noteCanEdit(data.status) && (
            <Can perm="billing.credit">
              <button type="button" className="btn" onClick={() => onEdit(data)}>{t('billing.credit.edit')}</button>
            </Can>
          )}
          {data && noteCanApprove(data.status) && (
            <Can perm="billing.credit">
              <button type="button" className="btn flow" disabled={approve.isPending} onClick={() => void doApprove()}>{t('billing.credit.approve')}</button>
            </Can>
          )}
          {data && noteCanApply(data.status, data.balance) && (
            <Can perm="billing.credit">
              <button type="button" className="btn flow" onClick={() => setApplying(true)}>{t('billing.credit.apply')}</button>
            </Can>
          )}
        </>
      }
    >
      <div className="pb">
        {note.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(note.error).title}</p>
        ) : !data ? (
          <Spinner block />
        ) : (
          <>
            <dl className="sales-meta">
              <div><dt>{t('billing.col.status')}</dt><dd><Chip color={data.statusColor ?? undefined}>{data.statusLabel}</Chip></dd></div>
              <div><dt>{t('billing.col.client')}</dt><dd>{data.clientName}</dd></div>
              <div><dt>{t('billing.credit.reason')}</dt><dd>{data.reasonLabel}</dd></div>
              <div>
                <dt>{t('billing.credit.invoice')}</dt>
                <dd>
                  {data.invoicePublicId ? (
                    <button type="button" className="linklike" onClick={() => onOpenInvoice(data.invoicePublicId!)}>{data.invoiceNumber}</button>
                  ) : (
                    data.externalInvoiceRef ?? '—'
                  )}
                </dd>
              </div>
              <div><dt>{t('billing.credit.created')}</dt><dd>{f.dateTime(data.createdAtUtc ?? '')}</dd></div>
              {data.approvedAtUtc && <div><dt>{t('billing.credit.approvedAt')}</dt><dd>{f.dateTime(data.approvedAtUtc)}</dd></div>}
              {data.note && <div><dt>{t('billing.new.note')}</dt><dd>{data.note}</dd></div>}
            </dl>
            <p className="help">{t(`billing.credit.hint.${data.status}`)}</p>
            <DataTable label={t('billing.credit.lines')} columns={lineColumns} rows={data.lines ?? []} rowKey={(l) => String(l.id)} empty={t('billing.invoice.noLines')} />
            <p className="sales-totals" role="status">
              {t('billing.invoice.totals', { subtotal: f.money(data.subtotal ?? 0), tax: f.money(data.taxAmount ?? 0), total: f.money(data.total ?? 0) })}
              {(data.applied ?? 0) > 0 && ` · ${t('billing.credit.appliedBalance', { applied: f.money(data.applied ?? 0), balance: f.money(data.balance ?? 0) })}`}
            </p>
            {(data.applications ?? []).length > 0 && (
              <>
                <h3 className="exp-h">{t('billing.credit.applications')}</h3>
                <ul className="credit-apps">
                  {(data.applications ?? []).map((a) => (
                    <li key={a.id}>
                      <button type="button" className="linklike" onClick={() => onOpenInvoice(a.invoicePublicId!)}>{a.invoiceNumber}</button>
                      <span>{f.date(a.appliedDate ?? '')}</span>
                      <b>{f.money(a.amount ?? 0)}</b>
                    </li>
                  ))}
                </ul>
              </>
            )}
            {error && <p className="ferr" role="alert">{error}</p>}
          </>
        )}
      </div>
      {applying && data && <ApplyDialog note={data} onClose={() => setApplying(false)} />}
      {voiding && data && <VoidNoteDialog note={data} onClose={() => setVoiding(false)} />}
    </Modal>
  )
}

function VoidNoteDialog({ note, onClose }: { note: CreditNoteDto; onClose: () => void }) {
  const t = useT()
  const { voidNote } = useCreditMutations()
  const [reason, setReason] = useState('')
  const [problem, setProblem] = useState<string | null>(null)
  return (
    <ConfirmDialog
      open
      title={t('billing.credit.voidTitle', { number: note.number ?? '' })}
      message={
        <>
          <p>{t('billing.credit.voidBody')}</p>
          <div className="f">
            <label htmlFor="void-note-reason">{t('billing.credit.voidReason')}</label>
            <input id="void-note-reason" value={reason} maxLength={250} autoComplete="off" onChange={(e) => setReason(e.target.value)} />
          </div>
          {problem && <p className="ferr" role="alert">{problem}</p>}
        </>
      }
      confirmLabel={t('billing.credit.void')}
      tone="danger"
      onConfirm={async () => {
        const bad = voidPaymentError(reason)
        if (bad) {
          const msg = t(`billing.credit.errors.void.${bad}`)
          setProblem(msg)
          throw new Error(msg)
        }
        setProblem(null)
        await voidNote.mutateAsync({ id: note.publicId!, reason: reason.trim() })
        toast.success(t('billing.credit.voidedToast', { number: note.number ?? '' }))
      }}
      onClose={onClose}
    />
  )
}

/** Las facturas del cliente (la lista admite el filtro por cliente; se trae una página grande y se filtra aquí por estatus). */
function useClientInvoices(clientPublicId: string | null) {
  return useQuery({
    queryKey: ['billing', 'client-invoices', clientPublicId],
    queryFn: () => unwrap(api.GET('/api/v1/billing/invoices', { params: { query: { clientPublicId: clientPublicId!, take: 200 } } })),
    enabled: clientPublicId !== null,
  })
}

function ApplyDialog({ note, onClose }: { note: CreditNoteDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const { apply } = useCreditMutations()
  const invoices = useClientInvoices(note.clientPublicId ?? null)
  const open = (invoices.data?.items ?? []).filter((i) => (i.status === 'ISSUED' || i.status === 'OVERDUE') && (i.balance ?? 0) > 0)
  const [invoiceId, setInvoiceId] = useState('')
  const [amount, setAmount] = useState('')
  const [error, setError] = useState<string | null>(null)
  const chosen = open.find((i) => i.publicId === invoiceId) ?? null
  const noteBalance = note.balance ?? 0

  const pick = (id: string) => {
    setInvoiceId(id)
    const inv = open.find((i) => i.publicId === id)
    setAmount(inv ? String(defaultApplyAmount(noteBalance, inv.balance ?? 0)) : '')
  }

  const save = async () => {
    if (!chosen) return setError(t('billing.credit.errors.apply.invoice'))
    const problem = applyCreditError(amount, noteBalance, chosen.balance ?? 0)
    if (problem) return setError(t(`billing.credit.errors.apply.${problem}`))
    setError(null)
    try {
      await apply.mutateAsync({ id: note.publicId!, invoicePublicId: chosen.publicId!, amount: parseAmount(amount)! })
      toast.success(t('billing.credit.applied'))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="sm"
      title={t('billing.credit.applyTitle', { number: note.number ?? '' })}
      onClose={onClose}
      dismissible={!apply.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={apply.isPending}>{t('billing.close')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={apply.isPending || !chosen}>{apply.isPending ? t('common.loading') : t('billing.credit.applySave')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('billing.credit.applyHelp', { balance: f.money(noteBalance), client: note.clientName ?? '' })}</p>
        {invoices.isPending ? (
          <Spinner />
        ) : open.length === 0 ? (
          <p className="help" role="status">{t('billing.credit.applyNone')}</p>
        ) : (
          <>
            <div className="f">
              <label htmlFor="ap-invoice">{t('billing.credit.invoice')}</label>
              <select id="ap-invoice" value={invoiceId} onChange={(e) => pick(e.target.value)}>
                <option value="">{t('billing.credit.chooseInvoice')}</option>
                {open.map((i) => (
                  <option key={i.publicId} value={i.publicId}>{`${i.number} · ${t('billing.credit.invoiceBalance', { balance: f.money(i.balance ?? 0) })}`}</option>
                ))}
              </select>
            </div>
            <div className="f">
              <label htmlFor="ap-amount">{t('billing.pay.amount')}</label>
              <input id="ap-amount" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
            </div>
          </>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

// ---------------------------------------------------------------- editor

type Mode = 'invoice' | 'free'

interface EditorSeed {
  /** Factura sobre la que se crea la nota (desde su ficha) o a la que ya pertenece la nota que se cambia. */
  invoicePublicId?: string | null
  /** La nota que se cambia (borrador). */
  note?: CreditNoteDto | null
}

export function CreditEditor({ seed, onClose, onSaved }: { seed: EditorSeed; onClose: () => void; onSaved: (id: string) => void }) {
  const t = useT()
  const f = useFormat()
  const { create, update } = useCreditMutations()
  const editing = seed.note ?? null
  const [mode, setMode] = useState<Mode>(seed.invoicePublicId || editing?.invoicePublicId ? 'invoice' : editing ? 'free' : 'invoice')
  const [invoiceId, setInvoiceId] = useState<string | null>(seed.invoicePublicId ?? editing?.invoicePublicId ?? null)
  const [clientId, setClientId] = useState<string | null>(editing?.clientPublicId ?? null)
  const [search, setSearch] = useState('')
  const q = useDebounced(search.trim())
  const found = useInvoices('', q, 0, 15)
  const invoice = useInvoice(invoiceId)
  const [form, setForm] = useState<CreditForm>(() => ({
    reason: editing?.reason ?? '',
    note: editing?.note ?? '',
    externalInvoiceRef: editing?.externalInvoiceRef ?? '',
    lines: editing && !editing.invoicePublicId ? (editing.lines ?? []).map(freeLine) : [blankCreditLine()],
  }))
  const [error, setError] = useState<string | null>(null)
  const onInvoice = mode === 'invoice'
  const inv: InvoiceDto | undefined = invoice.data
  const locked = editing !== null || !!seed.invoicePublicId
  // en una nota sobre una factura las líneas salen de la factura (las que ya estaban en la nota que se cambia, marcadas) y lo que el usuario cambia se guarda aparte por línea
  const [overrides, setOverrides] = useState<Record<number, Partial<CreditLineForm>>>({})
  const invoiceLines = useMemo(() => (inv?.lines ?? []).map((il) => ({ ...fromInvoiceLine(il, editing), ...overrides[il.id ?? -1] })), [inv, editing, overrides])
  const lines = onInvoice ? invoiceLines : form.lines
  const set = (patch: Partial<CreditForm>) => setForm((x) => ({ ...x, ...patch }))
  const setLine = (i: number, patch: Partial<CreditLineForm>) => {
    if (onInvoice) {
      const id = invoiceLines[i].invoiceLineId ?? -1
      setOverrides((o) => ({ ...o, [id]: { ...o[id], ...patch } }))
    } else {
      setForm((x) => ({ ...x, lines: x.lines.map((l, j) => (j === i ? { ...l, ...patch } : l)) }))
    }
  }
  const effective: CreditForm = { ...form, lines }
  const total = lines.filter((l) => !onInvoice || l.included).reduce((sum, l) => sum + lineValue(l), 0)
  const candidates = (found.data?.items ?? []).filter((i) => invoiceCanCredit(i.status))

  const save = async () => {
    if (!onInvoice && !clientId && !editing) return setError(t('billing.credit.errors.client'))
    if (onInvoice && !invoiceId) return setError(t('billing.credit.errors.invoice'))
    const problem = creditFormError(effective, onInvoice)
    if (problem) return setError(t(`billing.credit.errors.${problem}`))
    setError(null)
    try {
      const body = toCreditRequest(effective, onInvoice)
      const saved = editing
        ? await update.mutateAsync({ id: editing.publicId!, body })
        : await create.mutateAsync({ ...body, invoicePublicId: onInvoice ? invoiceId : undefined, clientPublicId: onInvoice ? undefined : clientId })
      toast.success(t(editing ? 'billing.credit.saved' : 'billing.credit.createdToast', { number: saved.number ?? '' }))
      onSaved(saved.publicId!)
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  const busy = create.isPending || update.isPending

  return (
    <Modal
      open
      size="lg"
      title={t(editing ? 'billing.credit.editTitle' : 'billing.credit.newTitle')}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('billing.close')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={busy}>{busy ? t('common.loading') : t('billing.credit.saveDraft')}</button>
        </>
      }
    >
      <div className="pb">
        {!locked && (
          <div className="credit-mode" role="radiogroup" aria-label={t('billing.credit.mode')}>
            <label><input type="radio" name="credit-mode" checked={onInvoice} onChange={() => setMode('invoice')} /> {t('billing.credit.modeInvoice')}</label>
            <label><input type="radio" name="credit-mode" checked={!onInvoice} onChange={() => (setMode('free'), setForm((x) => ({ ...x, lines: [blankCreditLine()] })))} /> {t('billing.credit.modeFree')}</label>
          </div>
        )}
        {onInvoice && !locked && !invoiceId && (
          <>
            <QBox value={search} onChange={setSearch} placeholder={t('billing.invoices.search')} />
            <ul className="credit-pick" aria-label={t('billing.credit.chooseInvoice')}>
              {found.isPending ? <li><Spinner /></li> : candidates.length === 0 ? <li className="help">{t('billing.invoices.empty')}</li> : candidates.map((i: InvoiceListItemDto) => (
                <li key={i.publicId}>
                  <button type="button" className="btn" onClick={() => setInvoiceId(i.publicId!)}>
                    <b>{i.number}</b> · {i.clientName} · {f.money(i.total ?? 0)}
                  </button>
                </li>
              ))}
            </ul>
          </>
        )}
        {onInvoice && invoiceId && (
          <p className="help">
            {t('billing.credit.onInvoice', { number: inv?.number ?? '…', client: inv?.clientName ?? '' })}
            {!locked && <> <button type="button" className="linklike" onClick={() => setInvoiceId(null)}>{t('billing.credit.changeInvoice')}</button></>}
          </p>
        )}
        {!onInvoice && !editing && (
          <div className="f">
            <label htmlFor="cr-client">{t('billing.col.client')}</label>
            <ClientPicker id="cr-client" value={clientId} onChange={setClientId} />
          </div>
        )}
        {!onInvoice && (
          <div className="f">
            <label htmlFor="cr-ref">{t('billing.credit.externalRef')}</label>
            <input id="cr-ref" value={form.externalInvoiceRef} maxLength={80} autoComplete="off" onChange={(e) => set({ externalInvoiceRef: e.target.value })} />
          </div>
        )}
        <div className="f">
          <label htmlFor="cr-reason">{t('billing.credit.reason')}</label>
          <select id="cr-reason" value={form.reason} onChange={(e) => set({ reason: e.target.value })}>
            <option value="">{t('billing.credit.chooseReason')}</option>
            {CREDIT_REASONS.map((r) => (
              <option key={r} value={r}>{t(`billing.credit.reasons.${r}`)}</option>
            ))}
          </select>
        </div>

        <h3 className="exp-h">{t('billing.credit.lines')}</h3>
        {onInvoice && !inv ? (
          invoiceId ? <Spinner /> : <p className="help">{t('billing.credit.pickFirst')}</p>
        ) : (
          <>
            <p className="help">{t(onInvoice ? 'billing.credit.linesHelpInvoice' : 'billing.credit.linesHelpFree')}</p>
            <div className="credit-lines-head" aria-hidden="true">
              <span>{t(onInvoice ? 'billing.credit.line' : 'billing.col.description')}</span>
              <span>{t('billing.col.qty')}</span>
              <span>{t('billing.col.unit')}</span>
              {!onInvoice && <span>{t('billing.credit.charge')}</span>}
            </div>
            <ol className="credit-lines" aria-label={t('billing.credit.lines')}>
              {lines.map((l, i) => (
                <li key={i}>
                  {onInvoice ? (
                    <label className="credit-incl">
                      <input type="checkbox" checked={l.included} aria-label={t('billing.credit.include', { n: i + 1 })} onChange={(e) => setLine(i, { included: e.target.checked })} />
                      <span>{l.description}</span>
                    </label>
                  ) : (
                    <input aria-label={t('billing.credit.descriptionOf', { n: i + 1 })} placeholder={t('billing.col.description')} value={l.description} maxLength={250} autoComplete="off" onChange={(e) => setLine(i, { description: e.target.value })} />
                  )}
                  <input aria-label={t('billing.credit.qtyOf', { n: i + 1 })} inputMode="decimal" value={l.quantity} onChange={(e) => setLine(i, { quantity: e.target.value })} disabled={onInvoice && !l.included} />
                  <input aria-label={t('billing.credit.unitOf', { n: i + 1 })} inputMode="decimal" value={l.unitAmount} onChange={(e) => setLine(i, { unitAmount: e.target.value })} disabled={onInvoice && !l.included} />
                  {!onInvoice && (
                    <select aria-label={t('billing.credit.chargeOf', { n: i + 1 })} value={l.chargeType} onChange={(e) => setLine(i, { chargeType: e.target.value })}>
                      {CREDIT_CHARGE_TYPES.map((c) => (
                        <option key={c} value={c}>{t(`billing.credit.charges.${c}`)}</option>
                      ))}
                    </select>
                  )}
                  {!onInvoice && lines.length > 1 && (
                    <button type="button" className="btn danger" aria-label={t('billing.credit.removeLine', { n: i + 1 })} onClick={() => set({ lines: form.lines.filter((_, j) => j !== i) })}>✕</button>
                  )}
                </li>
              ))}
            </ol>
            {!onInvoice && <button type="button" className="btn" onClick={() => set({ lines: [...form.lines, blankCreditLine()] })}>{t('billing.credit.addLine')}</button>}
            <p className="sales-totals" role="status">{t('billing.credit.estimate', { total: f.money(total) })}</p>
          </>
        )}
        <div className="f">
          <label htmlFor="cr-note">{t('billing.new.note')}</label>
          <input id="cr-note" value={form.note} maxLength={500} autoComplete="off" onChange={(e) => set({ note: e.target.value })} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

function freeLine(l: CreditNoteLineDto): CreditLineForm {
  return { invoiceLineId: null, description: l.description ?? '', quantity: String(l.quantity ?? 1), unitAmount: String(l.unitAmount ?? ''), chargeType: l.chargeType ?? 'FREIGHT', included: true }
}

/** Una línea de la factura en el formulario: la que ya está en la nota que se cambia sale marcada con lo que lleva; las demás, sin marcar y con lo facturado. */
function fromInvoiceLine(il: InvoiceDto['lines'][number], editing: CreditNoteDto | null): CreditLineForm {
  const inNote = editing?.lines?.find((l) => l.invoiceLineId === il.id)
  return {
    invoiceLineId: il.id ?? null,
    description: il.description ?? '',
    quantity: String(inNote?.quantity ?? il.quantity ?? 1),
    unitAmount: String(inNote?.unitAmount ?? il.unitAmount ?? ''),
    chargeType: '',
    included: editing ? !!inNote : true,
  }
}
