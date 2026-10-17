// Devoluciones de venta (Lote 82): la lista, la ficha (autorizar, rechazar, cancelar, recibir con lo bueno y lo dañado, acreditar), el editor para pedir una devolución de una orden despachada y la
// ventana de devolución. Pedir y cancelar piden sales.return; autorizar, rechazar y la ventana, sales.return.authorize; recibir, warehouse.receive; acreditar, billing.credit.
import { useEffect, useMemo, useState } from 'react'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, Modal, Panel, QBox, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { IconCart } from '../../kernel/ui/screenIcons'
import { useSalesOrders } from './salesApi'
import {
  salesReturnPageReader,
  useReturnMutations,
  useReturnSettings,
  useReturnable,
  useSalesReturn,
  useSalesReturns,
  type SalesReturnDto,
  type SalesReturnLineDto,
  type SalesReturnListItemDto,
} from './returnsApi'
import {
  RETURN_REASONS,
  RETURN_STATUSES,
  receiveFormError,
  returnCanAuthorize,
  returnCanCancel,
  returnCanCredit,
  returnCanReceive,
  returnCanReject,
  returnFormError,
  toReceiveRequest,
  toReturnRequest,
  windowDaysError,
  type ReceiveForm,
  type ReturnForm,
} from './returnRules'

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

// ---------------------------------------------------------------- lista

export function ReturnsTab({ onOpen, onNew }: { onOpen: (id: string) => void; onNew: () => void }) {
  const t = useT()
  const f = useFormat()
  const [status, setStatus] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const search = useDebounced(q.trim())
  const list = useSalesReturns(status, search, (page - 1) * pageSize, pageSize)
  const columns = useMemo<DataColumn<SalesReturnListItemDto>[]>(
    () => [
      { id: 'number', header: t('salesReturns.number'), cell: (r) => <b>{r.number}</b>, exportValue: (r) => r.number ?? '', sortValue: (r) => r.number ?? '', card: 'title' },
      { id: 'order', header: t('salesReturns.order'), cell: (r) => r.orderNumber, exportValue: (r) => r.orderNumber ?? '', sortValue: (r) => r.orderNumber ?? '' },
      { id: 'client', header: t('sales.col.client'), cell: (r) => r.clientName, exportValue: (r) => r.clientName ?? '', sortValue: (r) => r.clientName ?? '' },
      { id: 'reason', header: t('salesReturns.reason'), cell: (r) => r.reasonLabel, exportValue: (r) => r.reasonLabel ?? '', sortValue: (r) => r.reasonLabel ?? '' },
      {
        id: 'status',
        header: t('sales.col.status'),
        cell: (r) => (
          <>
            <Chip color={r.statusColor ?? undefined}>{r.statusLabel}</Chip>
            {r.outsideWindow && <> <Chip tone="warn">{t('salesReturns.outside')}</Chip></>}
          </>
        ),
        exportValue: (r) => r.statusLabel ?? '',
        sortValue: (r) => r.statusLabel ?? '',
      },
      { id: 'requested', header: t('salesReturns.requestedQty'), cell: (r) => f.number(r.qtyRequested ?? 0), exportValue: (r) => r.qtyRequested ?? 0, sortValue: (r) => r.qtyRequested ?? 0, align: 'end' },
      { id: 'received', header: t('salesReturns.receivedQty'), cell: (r) => f.number(r.qtyReceived ?? 0), exportValue: (r) => r.qtyReceived ?? 0, sortValue: (r) => r.qtyReceived ?? 0, align: 'end' },
      { id: 'date', header: t('salesReturns.requestedAt'), cell: (r) => f.date(r.requestedAtUtc ?? ''), exportValue: (r) => r.requestedAtUtc ?? '', sortValue: (r) => r.requestedAtUtc ?? '' },
    ],
    [t, f],
  )
  return (
    <>
      <Panel
        flush
        icon={<IconCart />}
        title={t('salesReturns.panel')}
        badge={list.isPending ? undefined : list.data?.total}
        actions={
          <Can perm="sales.return">
            <button type="button" className="btn flow" onClick={onNew}>{t('salesReturns.new')}</button>
          </Can>
        }
      >
        <div className="pb">
          <div className="qrow tax-filters">
            <QBox value={q} onChange={(v) => (setQ(v), setPage(1))} placeholder={t('salesReturns.search')} />
            <select aria-label={t('sales.orders.filterStatus')} value={status} onChange={(e) => (setStatus(e.target.value), setPage(1))}>
              <option value="">{t('sales.orders.allStatuses')}</option>
              {RETURN_STATUSES.map((s) => (
                <option key={s} value={s}>{t(`salesReturns.statuses.${s}`)}</option>
              ))}
            </select>
          </div>
          {list.isError ? (
            <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
          ) : list.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('salesReturns.panel')}
              columns={columns}
              rows={list.data?.items ?? []}
              rowKey={(r) => r.publicId ?? ''}
              page={page}
              pageSize={pageSize}
              total={list.data?.total ?? 0}
              onPage={setPage}
              onPageSize={(s) => (setPageSize(s), setPage(1))}
              onRowClick={(r) => onOpen(r.publicId!)}
              empty={t('salesReturns.empty')}
              exportRows={() => fetchAllPages<SalesReturnListItemDto>(salesReturnPageReader(status, search))}
            />
          )}
        </div>
      </Panel>
      <WindowPanel />
    </>
  )
}

/** La ventana de devolución de la compañía (días desde el último despacho); la cambia quien puede autorizar. */
function WindowPanel() {
  const t = useT()
  const settings = useReturnSettings()
  const { updateSettings } = useReturnMutations()
  const canEdit = useCan('sales.return.authorize')
  const [text, setText] = useState<string | null>(null)
  const shown = text ?? String(settings.data?.windowDays ?? '')
  const [error, setError] = useState<string | null>(null)
  if (!settings.data) return null
  const save = async () => {
    if (windowDaysError(shown)) return setError(t('salesReturns.windowError'))
    setError(null)
    try {
      await updateSettings.mutateAsync(Number(shown))
      setText(null)
      toast.success(t('salesReturns.windowSaved'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <div className="ret-window">
      <div className="f">
        <label htmlFor="ret-window">{t('salesReturns.window')}</label>
        <input id="ret-window" inputMode="numeric" value={shown} disabled={!canEdit} onChange={(e) => setText(e.target.value)} />
      </div>
      {canEdit && <button type="button" className="btn" onClick={() => void save()} disabled={updateSettings.isPending}>{t('salesReturns.windowSave')}</button>}
      <p className="help">{t('salesReturns.windowHelp')}</p>
      {error && <p className="ferr" role="alert">{error}</p>}
    </div>
  )
}

// ---------------------------------------------------------------- ficha

export function ReturnModal({ id, onClose }: { id: string; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const ret = useSalesReturn(id)
  const { credit } = useReturnMutations()
  const [dialog, setDialog] = useState<'authorize' | 'reject' | 'cancel' | 'receive' | 'credit' | null>(null)
  const [error, setError] = useState<string | null>(null)
  const data = ret.data
  const received = (data?.lines ?? []).reduce((s, l) => s + (l.qtyReceived ?? 0), 0)
  const pending = (data?.lines ?? []).reduce((s, l) => s + (l.pending ?? 0), 0)

  const columns = useMemo<DataColumn<SalesReturnLineDto>[]>(
    () => [
      { id: 'sku', header: t('sales.col.product'), cell: (l) => (<><b>{l.sku}</b> {l.productName}</>), exportValue: (l) => `${l.sku} ${l.productName}`, sortValue: (l) => l.sku ?? '', card: 'title' },
      { id: 'req', header: t('salesReturns.requestedQty'), cell: (l) => f.number(l.qtyRequested ?? 0), sortValue: (l) => l.qtyRequested ?? 0, align: 'end' },
      { id: 'rec', header: t('salesReturns.receivedQty'), cell: (l) => f.number(l.qtyReceived ?? 0), sortValue: (l) => l.qtyReceived ?? 0, align: 'end' },
      { id: 'dam', header: t('salesReturns.damagedQty'), cell: (l) => f.number(l.qtyDamaged ?? 0), sortValue: (l) => l.qtyDamaged ?? 0, align: 'end' },
      { id: 'cred', header: t('salesReturns.creditedQty'), cell: (l) => f.number(l.qtyCredited ?? 0), sortValue: (l) => l.qtyCredited ?? 0, align: 'end' },
      { id: 'pend', header: t('salesReturns.pendingQty'), cell: (l) => f.number(l.pending ?? 0), sortValue: (l) => l.pending ?? 0, align: 'end' },
    ],
    [t, f],
  )

  return (
    <Modal
      open
      size="lg"
      title={t('salesReturns.title', { number: data?.number ?? '' })}
      onClose={onClose}
      footer={
        <>
          {data && returnCanReject(data.status) && (
            <Can perm="sales.return.authorize">
              <button type="button" className="btn danger" onClick={() => setDialog('reject')}>{t('salesReturns.reject')}</button>
            </Can>
          )}
          {data && returnCanCancel(data.status, received) && (
            <Can perm="sales.return">
              <button type="button" className="btn danger" onClick={() => setDialog('cancel')}>{t('salesReturns.cancel')}</button>
            </Can>
          )}
          <button type="button" className="btn" onClick={onClose}>{t('sales.close')}</button>
          {data && returnCanAuthorize(data.status) && (
            <Can perm="sales.return.authorize">
              <button type="button" className="btn flow" onClick={() => setDialog('authorize')}>{t('salesReturns.authorize')}</button>
            </Can>
          )}
          {data && returnCanReceive(data.status, pending) && (
            <Can perm="warehouse.receive">
              <button type="button" className="btn flow" onClick={() => setDialog('receive')}>{t('salesReturns.receive')}</button>
            </Can>
          )}
          {data && returnCanCredit(data.status) && (
            <Can perm="billing.credit">
              <button type="button" className="btn flow" onClick={() => setDialog('credit')}>{t('salesReturns.credit')}</button>
            </Can>
          )}
        </>
      }
    >
      <div className="pb">
        {ret.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(ret.error).title}</p>
        ) : !data ? (
          <Spinner block />
        ) : (
          <>
            <dl className="sales-meta">
              <div><dt>{t('sales.col.status')}</dt><dd><Chip color={data.statusColor ?? undefined}>{data.statusLabel}</Chip>{data.outsideWindow && <> <Chip tone="warn">{t('salesReturns.outside')}</Chip></>}</dd></div>
              <div><dt>{t('salesReturns.order')}</dt><dd>{data.orderNumber}</dd></div>
              <div><dt>{t('sales.col.client')}</dt><dd>{data.clientName}</dd></div>
              <div><dt>{t('salesReturns.warehouse')}</dt><dd>{data.warehouseName}</dd></div>
              <div><dt>{t('salesReturns.reason')}</dt><dd>{data.reasonLabel}</dd></div>
              <div><dt>{t('salesReturns.requestedAt')}</dt><dd>{f.dateTime(data.requestedAtUtc ?? '')}</dd></div>
              {data.authorizedAtUtc && <div><dt>{t('salesReturns.authorizedAt')}</dt><dd>{f.dateTime(data.authorizedAtUtc)}</dd></div>}
              {data.note && <div><dt>{t('salesReturns.note')}</dt><dd>{data.note}</dd></div>}
              {data.authorizationNote && <div><dt>{t('salesReturns.authNote')}</dt><dd>{data.authorizationNote}</dd></div>}
            </dl>
            <p className="help">{t(`salesReturns.hint.${data.status}`)}</p>
            <DataTable label={t('salesReturns.lines')} columns={columns} rows={data.lines ?? []} rowKey={(l) => String(l.id)} empty={t('salesReturns.noLines')} />
            {(data.receipts ?? []).length > 0 && (
              <p className="help">{t('salesReturns.receipts')}: {(data.receipts ?? []).map((r) => r.number).join(', ')}</p>
            )}
            {(data.creditNotes ?? []).length > 0 && (
              <p className="help">{t('salesReturns.creditNotes')}: {(data.creditNotes ?? []).map((n) => `${n.number} (${f.money(n.total ?? 0)})`).join(', ')}</p>
            )}
            {error && <p className="ferr" role="alert">{error}</p>}
          </>
        )}
      </div>
      {dialog === 'authorize' && data && <AuthorizeDialog ret={data} onClose={() => setDialog(null)} />}
      {dialog === 'reject' && data && <RejectDialog ret={data} onClose={() => setDialog(null)} />}
      {dialog === 'cancel' && data && <CancelDialog ret={data} onClose={() => setDialog(null)} />}
      {dialog === 'receive' && data && <ReceiveDialog ret={data} onClose={() => setDialog(null)} />}
      <ConfirmDialog
        open={dialog === 'credit'}
        title={t('salesReturns.creditTitle', { number: data?.number ?? '' })}
        message={<p>{t('salesReturns.creditBody')}</p>}
        confirmLabel={t('salesReturns.credit')}
        onConfirm={async () => {
          try {
            const r = await credit.mutateAsync(id)
            toast.success(t('salesReturns.credited', { count: (r.creditNotes ?? []).length }))
          } catch (e) {
            setError(applyProblemDetails(e).title)
            throw e
          }
        }}
        onClose={() => setDialog(null)}
      />
    </Modal>
  )
}

function TextDialog({ title, label, help, confirmLabel, max, required, tone, onConfirm, onClose }: {
  title: string
  label: string
  help?: string
  confirmLabel: string
  max: number
  required: boolean
  tone?: 'danger' | 'flow'
  onConfirm: (text: string) => Promise<void>
  onClose: () => void
}) {
  const t = useT()
  const [text, setText] = useState('')
  const [problem, setProblem] = useState<string | null>(null)
  return (
    <ConfirmDialog
      open
      title={title}
      message={
        <>
          {help && <p>{help}</p>}
          <div className="f">
            <label htmlFor="ret-text">{label}</label>
            <input id="ret-text" value={text} maxLength={max} autoComplete="off" onChange={(e) => setText(e.target.value)} />
          </div>
          {problem && <p className="ferr" role="alert">{problem}</p>}
        </>
      }
      confirmLabel={confirmLabel}
      tone={tone}
      onConfirm={async () => {
        if (required && text.trim() === '') {
          const msg = t('salesReturns.reasonRequired')
          setProblem(msg)
          throw new Error(msg)
        }
        setProblem(null)
        await onConfirm(text.trim())
      }}
      onClose={onClose}
    />
  )
}

function AuthorizeDialog({ ret, onClose }: { ret: SalesReturnDto; onClose: () => void }) {
  const t = useT()
  const { authorize } = useReturnMutations()
  return (
    <TextDialog
      title={t('salesReturns.authorizeTitle', { number: ret.number ?? '' })}
      help={t('salesReturns.authorizeBody')}
      label={t('salesReturns.authNote')}
      confirmLabel={t('salesReturns.authorize')}
      max={500}
      required={false}
      onConfirm={async (note) => {
        await authorize.mutateAsync({ id: ret.publicId!, note: note || undefined })
        toast.success(t('salesReturns.authorized', { number: ret.number ?? '' }))
      }}
      onClose={onClose}
    />
  )
}

function RejectDialog({ ret, onClose }: { ret: SalesReturnDto; onClose: () => void }) {
  const t = useT()
  const { reject } = useReturnMutations()
  return (
    <TextDialog
      title={t('salesReturns.rejectTitle', { number: ret.number ?? '' })}
      label={t('salesReturns.rejectReason')}
      confirmLabel={t('salesReturns.reject')}
      tone="danger"
      max={500}
      required
      onConfirm={async (reason) => {
        await reject.mutateAsync({ id: ret.publicId!, reason })
        toast.success(t('salesReturns.rejected', { number: ret.number ?? '' }))
      }}
      onClose={onClose}
    />
  )
}

function CancelDialog({ ret, onClose }: { ret: SalesReturnDto; onClose: () => void }) {
  const t = useT()
  const { cancel } = useReturnMutations()
  return (
    <TextDialog
      title={t('salesReturns.cancelTitle', { number: ret.number ?? '' })}
      help={t('salesReturns.cancelBody')}
      label={t('salesReturns.cancelComment')}
      confirmLabel={t('salesReturns.cancel')}
      tone="danger"
      max={250}
      required={false}
      onConfirm={async (comment) => {
        await cancel.mutateAsync({ id: ret.publicId!, comment: comment || undefined })
        toast.success(t('salesReturns.cancelled', { number: ret.number ?? '' }))
      }}
      onClose={onClose}
    />
  )
}

// ---------------------------------------------------------------- recibir

function ReceiveDialog({ ret, onClose }: { ret: SalesReturnDto; onClose: () => void }) {
  const t = useT()
  const { receive } = useReturnMutations()
  const [form, setForm] = useState<ReceiveForm>(() => ({
    closeRest: false,
    lines: (ret.lines ?? []).filter((l) => (l.pending ?? 0) > 0).map((l) => ({ lineId: l.id!, pending: l.pending ?? 0, good: '', damaged: '', damageNote: '', discard: false, lot: '' })),
  }))
  const [error, setError] = useState<string | null>(null)
  const setLine = (i: number, patch: Partial<ReceiveForm['lines'][number]>) => setForm((x) => ({ ...x, lines: x.lines.map((l, j) => (j === i ? { ...l, ...patch } : l)) }))
  const names = new Map((ret.lines ?? []).map((l) => [l.id, `${l.sku} ${l.productName}`]))

  const save = async () => {
    const problem = receiveFormError(form)
    if (problem) return setError(t(`salesReturns.errors.receive.${problem}`))
    setError(null)
    try {
      await receive.mutateAsync({ id: ret.publicId!, body: toReceiveRequest(form) })
      toast.success(t('salesReturns.received', { number: ret.number ?? '' }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="lg"
      title={t('salesReturns.receiveTitle', { number: ret.number ?? '' })}
      onClose={onClose}
      dismissible={!receive.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={receive.isPending}>{t('sales.close')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={receive.isPending}>{receive.isPending ? t('common.loading') : t('salesReturns.receiveSave')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('salesReturns.receiveHelp')}</p>
        <ol className="ret-lines" aria-label={t('salesReturns.lines')}>
          {form.lines.map((l, i) => {
            const damaged = Number(l.damaged.replace(',', '.')) > 0
            return (
              <li key={l.lineId}>
                <div className="ret-line-head">
                  <b>{names.get(l.lineId)}</b>
                  <span className="help">{t('salesReturns.pendingOf', { n: l.pending })}</span>
                </div>
                <div className="ret-line-grid">
                  <div className="f">
                    <label htmlFor={`rg-${i}`}>{t('salesReturns.good')}</label>
                    <input id={`rg-${i}`} inputMode="decimal" aria-label={t('salesReturns.goodOf', { name: names.get(l.lineId) ?? '' })} value={l.good} onChange={(e) => setLine(i, { good: e.target.value })} />
                  </div>
                  <div className="f">
                    <label htmlFor={`rd-${i}`}>{t('salesReturns.damaged')}</label>
                    <input id={`rd-${i}`} inputMode="decimal" aria-label={t('salesReturns.damagedOf', { name: names.get(l.lineId) ?? '' })} value={l.damaged} onChange={(e) => setLine(i, { damaged: e.target.value })} />
                  </div>
                  <div className="f">
                    <label htmlFor={`rl-${i}`}>{t('salesReturns.lot')}</label>
                    <input id={`rl-${i}`} autoComplete="off" aria-label={t('salesReturns.lotOf', { name: names.get(l.lineId) ?? '' })} value={l.lot} onChange={(e) => setLine(i, { lot: e.target.value })} />
                  </div>
                </div>
                {damaged && (
                  <div className="ret-damage">
                    <div className="f">
                      <label htmlFor={`rn-${i}`}>{t('salesReturns.damageNote')}</label>
                      <input id={`rn-${i}`} aria-label={t('salesReturns.damageNoteOf', { name: names.get(l.lineId) ?? '' })} value={l.damageNote} maxLength={250} autoComplete="off" onChange={(e) => setLine(i, { damageNote: e.target.value })} />
                    </div>
                    <label className="ret-check">
                      <input type="checkbox" checked={l.discard} onChange={(e) => setLine(i, { discard: e.target.checked })} /> {t('salesReturns.discard')}
                    </label>
                  </div>
                )}
              </li>
            )
          })}
        </ol>
        <label className="ret-check">
          <input type="checkbox" checked={form.closeRest} onChange={(e) => setForm((x) => ({ ...x, closeRest: e.target.checked }))} /> {t('salesReturns.closeRest')}
        </label>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

// ---------------------------------------------------------------- pedir

export function ReturnEditor({ orderPublicId, onClose, onSaved }: { orderPublicId?: string | null; onClose: () => void; onSaved: (id: string) => void }) {
  const t = useT()
  const f = useFormat()
  const { create } = useReturnMutations()
  const canAuthorize = useCan('sales.return.authorize')
  const [orderId, setOrderId] = useState<string | null>(orderPublicId ?? null)
  const [search, setSearch] = useState('')
  const q = useDebounced(search.trim())
  const found = useSalesOrders('', q, 0, 15)
  const returnable = useReturnable(orderId)
  const [reason, setReason] = useState('')
  const [note, setNote] = useState('')
  const [confirmOutside, setConfirmOutside] = useState(false)
  const [quantities, setQuantities] = useState<Record<number, string>>({})
  const [error, setError] = useState<string | null>(null)
  const data = returnable.data
  const candidates = (found.data?.items ?? []).filter((o) => o.status === 'SHIPPED' || o.status === 'PARTIAL')

  const form: ReturnForm = {
    reason,
    note,
    confirmOutsideWindow: confirmOutside,
    lines: (data?.lines ?? []).map((l) => ({ salesOrderLineId: l.lineId!, quantity: quantities[l.lineId!] ?? '', returnable: l.returnable ?? 0, blocked: !!(l.isKit || l.isSerial) })),
  }

  const save = async () => {
    if (!orderId) return setError(t('salesReturns.errors.order'))
    const problem = returnFormError(form, data?.outsideWindow ?? false, canAuthorize)
    if (problem) return setError(t(`salesReturns.errors.${problem}`))
    setError(null)
    try {
      const saved = await create.mutateAsync(toReturnRequest(orderId, form))
      toast.success(t('salesReturns.created', { number: saved.number ?? '' }))
      onSaved(saved.publicId!)
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="lg"
      title={t('salesReturns.newTitle')}
      onClose={onClose}
      dismissible={!create.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={create.isPending}>{t('sales.close')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={create.isPending}>{create.isPending ? t('common.loading') : t('salesReturns.request')}</button>
        </>
      }
    >
      <div className="pb">
        {!orderId ? (
          <>
            <p className="help">{t('salesReturns.pickOrder')}</p>
            <QBox value={search} onChange={setSearch} placeholder={t('sales.orders.search')} />
            <ul className="credit-pick" aria-label={t('salesReturns.pickOrder')}>
              {found.isPending ? (
                <li><Spinner /></li>
              ) : candidates.length === 0 ? (
                <li className="help">{t('salesReturns.noOrders')}</li>
              ) : (
                candidates.map((o) => (
                  <li key={o.publicId}>
                    <button type="button" className="btn" onClick={() => setOrderId(o.publicId!)}>
                      <b>{o.number}</b> · {o.clientName} · {f.date(o.requestedDate ?? '')}
                    </button>
                  </li>
                ))
              )}
            </ul>
          </>
        ) : (
          <>
            <p className="help">
              {t('salesReturns.onOrder', { number: data?.orderNumber ?? '…' })}
              {!orderPublicId && <> <button type="button" className="linklike" onClick={() => (setOrderId(null), setQuantities({}))}>{t('salesReturns.changeOrder')}</button></>}
            </p>
            {returnable.isError ? (
              <p className="ferr" role="alert">{applyProblemDetails(returnable.error).title}</p>
            ) : !data ? (
              <Spinner />
            ) : (
              <>
                {data.outsideWindow && (
                  <div className="ret-outside" role="status">
                    <p>{t('salesReturns.outsideNotice', { days: data.windowDays ?? 0, date: data.lastDispatchDate ? f.date(data.lastDispatchDate) : '' })}</p>
                    {canAuthorize ? (
                      <label className="ret-check">
                        <input type="checkbox" checked={confirmOutside} onChange={(e) => setConfirmOutside(e.target.checked)} /> {t('salesReturns.confirmOutside')}
                      </label>
                    ) : (
                      <p className="help">{t('salesReturns.needsAuthorizer')}</p>
                    )}
                  </div>
                )}
                <div className="f">
                  <label htmlFor="ret-reason">{t('salesReturns.reason')}</label>
                  <select id="ret-reason" value={reason} onChange={(e) => setReason(e.target.value)}>
                    <option value="">{t('salesReturns.chooseReason')}</option>
                    {RETURN_REASONS.map((r) => (
                      <option key={r} value={r}>{t(`salesReturns.reasons.${r}`)}</option>
                    ))}
                  </select>
                </div>
                <h3 className="exp-h">{t('salesReturns.lines')}</h3>
                <p className="help">{t('salesReturns.linesHelp')}</p>
                <div className="ret-grid-head" aria-hidden="true">
                  <span>{t('sales.col.product')}</span>
                  <span>{t('salesReturns.shipped')}</span>
                  <span>{t('salesReturns.returned')}</span>
                  <span>{t('salesReturns.pendingQty')}</span>
                  <span>{t('salesReturns.returnable')}</span>
                  <span>{t('salesReturns.qtyToReturn')}</span>
                </div>
                <ol className="ret-lines ret-grid" aria-label={t('salesReturns.lines')}>
                  {(data.lines ?? []).map((l) => {
                    const blocked = !!(l.isKit || l.isSerial) || (l.returnable ?? 0) <= 0
                    return (
                      <li key={l.lineId}>
                        <span><b>{l.sku}</b> {l.productName}{l.isKit && <> <Chip tone="warn">{t('salesReturns.kit')}</Chip></>}{l.isSerial && <> <Chip tone="warn">{t('salesReturns.serial')}</Chip></>}</span>
                        <span>{f.number(l.shipped ?? 0)}</span>
                        <span>{f.number(l.returned ?? 0)}</span>
                        <span>{f.number(l.pending ?? 0)}</span>
                        <span>{f.number(l.returnable ?? 0)}</span>
                        <input
                          aria-label={t('salesReturns.qtyOf', { sku: l.sku ?? '' })}
                          inputMode="decimal"
                          disabled={blocked}
                          value={quantities[l.lineId!] ?? ''}
                          onChange={(e) => setQuantities((x) => ({ ...x, [l.lineId!]: e.target.value }))}
                        />
                      </li>
                    )
                  })}
                </ol>
                <div className="f">
                  <label htmlFor="ret-note">{t('salesReturns.note')}</label>
                  <input id="ret-note" value={note} maxLength={500} autoComplete="off" onChange={(e) => setNote(e.target.value)} />
                </div>
              </>
            )}
          </>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}
