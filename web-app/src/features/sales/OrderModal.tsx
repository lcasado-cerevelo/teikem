// Órdenes de venta (Lote 60): la ventana de una orden. En BORRADOR se arma y se edita (cliente, almacén, fecha requerida, notas y líneas con producto, cantidad
// y precio) y se «Solicita»: antes de reservar se comprueba el inventario y el crédito; si falta algo sale el diálogo de faltante (pedido / disponible /
// faltante) con las salidas que da el servidor (reservar lo disponible y dejar el resto en backorder, o volver a editar). Una orden SOLICITADA se ve con
// lo reservado y lo pendiente y permite cambiar la cantidad de una línea, surtir o cerrar con faltante y cancelar.
import { useMemo, useState } from 'react'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ClientPicker, ConfirmDialog, DataTable, Modal, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { PackModal } from '../warehouse/PackModal'
import { ProductPicker, WarehousePicker } from '../warehouse/pickers'
import { draftError, draftSubtotal, effectivePrice, lineAmount, parseNumber, toRequest, type LineDraft, type OrderDraft } from './salesRules'
import { useSalesMutations, useSalesOrder, type SalesAvailabilityDto, type SalesDispatchInfoDto, type SalesDispatchResultDto, type SalesOrderDto, type SalesOrderLineDto } from './salesApi'

let nextKey = 1

function draftOf(order: SalesOrderDto | null): OrderDraft {
  // una orden nueva arranca con una línea vacía para escoger el primer producto
  if (!order) return { clientPublicId: null, warehousePublicId: null, requiredDate: '', notes: '', lines: [{ key: nextKey++, productPublicId: null, sku: '', name: '', listPrice: null, quantity: '', price: '' }] }
  return {
    clientPublicId: order.clientPublicId ?? null,
    warehousePublicId: order.warehousePublicId ?? null,
    requiredDate: order.requiredDate ?? '',
    notes: order.notes ?? '',
    // el precio de una línea ya guardada viaja siempre (no se sabe si era el de lista)
    lines: (order.lines ?? []).map((l) => ({ key: nextKey++, productPublicId: l.productPublicId ?? null, sku: l.sku ?? '', name: l.productName ?? '', listPrice: null, quantity: String(l.qtyOrdered ?? ''), price: String(l.unitPrice ?? '') })),
  }
}

// ---------------------------------------------------------------- diálogo de faltante y crédito

function AvailabilityDialog({ order, check, onClose, onDone }: { order: SalesOrderDto; check: SalesAvailabilityDto; onClose: () => void; onDone: () => void }) {
  const t = useT()
  const f = useFormat()
  const { request } = useSalesMutations()
  const canOverride = useCan('orders.credit_override')
  const [override, setOverride] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const credit = check.credit
  const exceeded = !!credit?.exceeded
  const shortLines = (check.lines ?? []).filter((l) => (l.shortage ?? 0) > 0)
  const blockedByCredit = exceeded && !(override && canOverride)

  const go = async (allowBackorder: boolean) => {
    setError(null)
    try {
      await request.mutateAsync({ id: order.publicId!, allowBackorder, overrideCredit: exceeded && override ? true : undefined })
      toast.success(t('sales.requested', { number: order.number ?? '' }))
      onDone()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('sales.check.title', { number: order.number ?? '' })}
      onClose={onClose}
      dismissible={!request.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={request.isPending}>{t('sales.check.back')}</button>
          {shortLines.length > 0 ? (
            <button type="button" className="btn flow" disabled={request.isPending || blockedByCredit} onClick={() => void go(true)}>{t('sales.check.backorder')}</button>
          ) : (
            <button type="button" className="btn flow" disabled={request.isPending || blockedByCredit} onClick={() => void go(false)}>{t('sales.check.request')}</button>
          )}
        </>
      }
    >
      <div className="pb sales-form">
        {shortLines.length > 0 && (
          <>
            <p>{t('sales.check.shortage')}</p>
            <table className="sales-table">
              <thead>
                <tr>
                  <th scope="col">{t('sales.col.product')}</th>
                  <th scope="col" className="num">{t('sales.check.ordered')}</th>
                  <th scope="col" className="num">{t('sales.check.available')}</th>
                  <th scope="col" className="num">{t('sales.check.missing')}</th>
                </tr>
              </thead>
              <tbody>
                {shortLines.map((l) => (
                  <tr key={l.lineNo}>
                    <td><b>{l.sku}</b><div className="help">{l.productName}</div></td>
                    <td className="num">{f.number(l.ordered ?? 0)}</td>
                    <td className="num">{f.number(l.available ?? 0)}</td>
                    <td className="num"><b>{f.number(l.shortage ?? 0)}</b></td>
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="help">{t('sales.check.shortageHelp')}</p>
          </>
        )}
        {exceeded && (
          <div className="sales-credit" role="alert">
            <p><b>{credit?.message}</b></p>
            {canOverride ? (
              <label className="sw">
                <input type="checkbox" role="switch" checked={override} onChange={(e) => setOverride(e.target.checked)} />
                <span className="tk" aria-hidden="true" />
                <span>{t('sales.check.override')}</span>
              </label>
            ) : (
              <p className="help">{t('sales.check.needOverride')}</p>
            )}
          </div>
        )}
        {shortLines.length === 0 && !exceeded && <p>{t('sales.check.ok')}</p>}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

// ---------------------------------------------------------------- borrador

function DraftEditor({ order, onClose }: { order: SalesOrderDto | null; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const { create, update, check, cancel } = useSalesMutations()
  const [draft, setDraft] = useState<OrderDraft>(() => draftOf(order))
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState<SalesOrderDto | null>(null)
  const [checking, setChecking] = useState<{ order: SalesOrderDto; check: SalesAvailabilityDto } | null>(null)
  const [cancelling, setCancelling] = useState(false)
  const busy = create.isPending || update.isPending || check.isPending
  const set = (patch: Partial<OrderDraft>) => setDraft((d) => ({ ...d, ...patch }))
  const setLine = (key: number, patch: Partial<LineDraft>) => set({ lines: draft.lines.map((l) => (l.key === key ? { ...l, ...patch } : l)) })
  const current = saved ?? order

  const save = async (): Promise<SalesOrderDto | null> => {
    setError(null)
    const problem = draftError(draft)
    if (problem) {
      setError(t(`sales.errors.${problem}`))
      return null
    }
    try {
      const body = toRequest(draft)
      const dto = current ? await update.mutateAsync({ id: current.publicId!, body }) : await create.mutateAsync(body)
      setSaved(dto)
      return dto
    } catch (e) {
      setError(applyProblemDetails(e).title)
      return null
    }
  }

  const saveDraft = async () => {
    const dto = await save()
    if (!dto) return
    toast.success(t('sales.saved', { number: dto.number ?? '' }))
    onClose()
  }

  const saveAndRequest = async () => {
    const dto = await save()
    if (!dto) return
    try {
      const result = await check.mutateAsync(dto.publicId!)
      setChecking({ order: dto, check: result })   // una sola puerta: con o sin faltante, el diálogo muestra el resultado y confirma
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="lg"
      title={current ? t('sales.edit.title', { number: current.number ?? '' }) : t('sales.new')}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          {current && (
            <Can perm="sales.cancel">
              <button type="button" className="btn danger" onClick={() => setCancelling(true)} disabled={busy}>{t('sales.cancelOrder')}</button>
            </Can>
          )}
          <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('sales.close')}</button>
          <button type="button" className="btn" onClick={() => void saveDraft()} disabled={busy}>{t('sales.saveDraft')}</button>
          <button type="button" className="btn flow" onClick={() => void saveAndRequest()} disabled={busy}>{busy ? t('common.loading') : t('sales.saveAndRequest')}</button>
        </>
      }
    >
      <div className="pb sales-form">
        <div className="r2">
          <div className="f">
            <label htmlFor="so-client">{t('sales.client')}</label>
            <ClientPicker id="so-client" value={draft.clientPublicId} onChange={(id) => set({ clientPublicId: id })} />
          </div>
          <div className="f">
            <label htmlFor="so-wh">{t('sales.warehouse')}</label>
            <WarehousePicker id="so-wh" value={draft.warehousePublicId} onChange={(id) => set({ warehousePublicId: id })} />
            <p className="help">{t('sales.warehouseHelp')}</p>
          </div>
        </div>
        <div className="r2">
          <div className="f">
            <label htmlFor="so-req">{t('sales.requiredDate')}</label>
            <input id="so-req" type="date" value={draft.requiredDate} onChange={(e) => set({ requiredDate: e.target.value })} />
          </div>
          <div className="f">
            <label htmlFor="so-notes">{t('sales.notes')}</label>
            <input id="so-notes" value={draft.notes} maxLength={500} autoComplete="off" onChange={(e) => set({ notes: e.target.value })} />
          </div>
        </div>
        <fieldset className="tax-comps">
          <legend>{t('sales.lines')}</legend>
          {draft.lines.map((l, i) => (
            <div key={l.key} className="sales-line">
              <div className="f">
                <label htmlFor={`so-p-${l.key}`}>{t('sales.line.product', { n: i + 1 })}</label>
                <ProductPicker
                  id={`so-p-${l.key}`}
                  ownOnly
                  value={l.productPublicId}
                  onChange={(id, p) => setLine(l.key, { productPublicId: id, sku: p?.sku ?? '', name: p?.name ?? '', listPrice: p?.salePrice ?? null, price: '' })}
                />
              </div>
              <div className="f">
                <label htmlFor={`so-q-${l.key}`}>{t('sales.line.qty', { n: i + 1 })}</label>
                <input id={`so-q-${l.key}`} inputMode="decimal" value={l.quantity} onChange={(e) => setLine(l.key, { quantity: e.target.value })} />
              </div>
              <div className="f">
                <label htmlFor={`so-pr-${l.key}`}>{t('sales.line.price', { n: i + 1 })}</label>
                <input
                  id={`so-pr-${l.key}`}
                  inputMode="decimal"
                  value={l.price}
                  placeholder={l.listPrice === null ? '' : f.money(l.listPrice)}
                  onChange={(e) => setLine(l.key, { price: e.target.value })}
                />
              </div>
              <div className="f sales-amount" aria-label={t('sales.line.amount', { n: i + 1 })}>
                <span>{effectivePrice(l) === null || parseNumber(l.quantity) === null ? '—' : f.money(lineAmount(l))}</span>
              </div>
              <button type="button" className="btn ghost" aria-label={t('sales.line.remove', { n: i + 1 })} onClick={() => set({ lines: draft.lines.filter((x) => x.key !== l.key) })}>✕</button>
            </div>
          ))}
          <div className="tax-comps-foot">
            <button type="button" className="btn" onClick={() => set({ lines: [...draft.lines, { key: nextKey++, productPublicId: null, sku: '', name: '', listPrice: null, quantity: '', price: '' }] })}>{t('sales.addLine')}</button>
            <b role="status">{t('sales.subtotal', { n: f.money(draftSubtotal(draft)) })}</b>
          </div>
        </fieldset>
        <p className="help">{t('sales.priceHelp')}</p>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
      {checking && (
        <AvailabilityDialog
          order={checking.order}
          check={checking.check}
          onClose={() => setChecking(null)}
          onDone={() => {
            setChecking(null)
            onClose()
          }}
        />
      )}
      <ConfirmDialog
        open={cancelling}
        title={t('sales.cancelTitle', { number: current?.number ?? '' })}
        message={t('sales.cancelBody')}
        confirmLabel={t('sales.cancelOrder')}
        tone="danger"
        onConfirm={async () => {
          await cancel.mutateAsync({ id: current!.publicId! })
          toast.success(t('sales.cancelled', { number: current?.number ?? '' }))
          setCancelling(false)
          onClose()
        }}
        onClose={() => setCancelling(false)}
      />
    </Modal>
  )
}

// ---------------------------------------------------------------- orden solicitada (y las demás, solo lectura)

function QuantityModal({ order, line, onClose }: { order: SalesOrderDto; line: SalesOrderLineDto; onClose: () => void }) {
  const t = useT()
  const { modifyLine } = useSalesMutations()
  const [qty, setQty] = useState(String(line.qtyOrdered ?? ''))
  const [allow, setAllow] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const save = async () => {
    setError(null)
    const q = parseNumber(qty)
    if (q === null || q <= 0) return setError(t('sales.errors.quantity'))
    try {
      await modifyLine.mutateAsync({ id: order.publicId!, lineId: line.id!, quantity: q, allowBackorder: allow || undefined })
      toast.success(t('sales.lineChanged', { sku: line.sku ?? '' }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <Modal
      open
      size="sm"
      title={t('sales.qtyTitle', { sku: line.sku ?? '' })}
      onClose={onClose}
      dismissible={!modifyLine.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={modifyLine.isPending}>{t('common.cancel')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={modifyLine.isPending}>{t('sales.qtySave')}</button>
        </>
      }
    >
      <div className="pb sales-form">
        <p className="help">{t('sales.qtyHelp', { reserved: line.qtyReserved ?? 0, backordered: line.qtyBackordered ?? 0 })}</p>
        <div className="f">
          <label htmlFor="so-newqty">{t('sales.qtyNew')}</label>
          <input id="so-newqty" inputMode="decimal" value={qty} onChange={(e) => setQty(e.target.value)} />
        </div>
        <label className="sw">
          <input type="checkbox" role="switch" checked={allow} onChange={(e) => setAllow(e.target.checked)} />
          <span className="tk" aria-hidden="true" />
          <span>{t('sales.qtyAllowBackorder')}</span>
        </label>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

// ---------------------------------------------------------------- despachar (Lote 63)

/** Despacha contra la orden: por línea, cuánto sale (por omisión todo lo reservado). El sistema usa las posiciones y lotes que reservó; el aparato permite tomar de otra posición. */
function DispatchModal({ order, onClose, onDone }: { order: SalesOrderDto; onClose: () => void; onDone: (r: SalesDispatchResultDto) => void }) {
  const t = useT()
  const f = useFormat()
  const { dispatch } = useSalesMutations()
  const lines = (order.lines ?? []).filter((l) => (l.qtyReserved ?? 0) > 0)
  const [qty, setQty] = useState<Record<number, string>>(() => Object.fromEntries(lines.map((l) => [l.id!, String(l.qtyReserved)])))
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)

  const save = async () => {
    setError(null)
    const picked = lines.map((l) => ({ line: l, q: parseNumber(qty[l.id!] ?? '') })).filter((x) => x.q !== null && x.q > 0)
    if (lines.some((l) => (qty[l.id!] ?? '').trim() !== '' && parseNumber(qty[l.id!]) === null)) return setError(t('sales.errors.quantity'))
    if (picked.length === 0) return setError(t('sales.dispatch.errors.none'))
    const over = picked.find((x) => (x.q ?? 0) > (x.line.qtyReserved ?? 0))
    if (over) return setError(t('sales.dispatch.errors.more', { sku: over.line.sku ?? '', n: over.line.qtyReserved ?? 0 }))
    try {
      const result = await dispatch.mutateAsync({ id: order.publicId!, body: { lines: picked.map((x) => ({ lineId: x.line.id, quantity: x.q ?? 0 })), note: note.trim() || undefined } })
      onDone(result)
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('sales.dispatch.title', { number: order.number ?? '' })}
      onClose={onClose}
      dismissible={!dispatch.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={dispatch.isPending}>{t('common.cancel')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={dispatch.isPending}>{dispatch.isPending ? t('common.loading') : t('sales.dispatch.confirm')}</button>
        </>
      }
    >
      <div className="pb sales-form">
        <p className="help">{t('sales.dispatch.help')}</p>
        <table className="sales-table">
          <thead>
            <tr>
              <th scope="col">{t('sales.col.product')}</th>
              <th scope="col" className="num">{t('sales.col.reserved')}</th>
              <th scope="col" className="num">{t('sales.dispatch.qty')}</th>
            </tr>
          </thead>
          <tbody>
            {lines.map((l) => (
              <tr key={l.id}>
                <td><b>{l.sku}</b><div className="help">{l.productName}</div></td>
                <td className="num">{f.number(l.qtyReserved ?? 0)}</td>
                <td className="num">
                  <input aria-label={t('sales.dispatch.qtyOf', { sku: l.sku ?? '' })} inputMode="decimal" className="sales-qty" value={qty[l.id!] ?? ''} onChange={(e) => setQty((m) => ({ ...m, [l.id!]: e.target.value }))} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <div className="f">
          <label htmlFor="so-dnote">{t('sales.dispatch.note')}</label>
          <input id="so-dnote" value={note} maxLength={500} autoComplete="off" onChange={(e) => setNote(e.target.value)} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

/** ¿Qué sigue con lo despachado? Retiro (ahí termina), entrega (orden de transporte) o decidirlo después. */
function NextDialog({ result, onPickup, onDelivery, onLater }: { result: SalesDispatchResultDto; onPickup: () => void; onDelivery: () => void; onLater: () => void }) {
  const t = useT()
  return (
    <Modal
      open
      size="sm"
      title={t('sales.next.title', { number: result.pickBatchNumber ?? '' })}
      onClose={onLater}
      footer={<button type="button" className="btn" onClick={onLater}>{t('sales.next.later')}</button>}
    >
      <div className="pb sales-form">
        <p>{t('sales.next.question')}</p>
        <button type="button" className="btn" onClick={onPickup}>{t('sales.next.pickup')}</button>
        <button type="button" className="btn flow" onClick={onDelivery}>{t('sales.next.delivery')}</button>
        <p className="help">{t('sales.next.help')}</p>
      </div>
    </Modal>
  )
}

function OrderView({ order, onClose }: { order: SalesOrderDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const { cancel, fulfillLine, closeLine } = useSalesMutations()
  const [editing, setEditing] = useState<SalesOrderLineDto | null>(null)
  const [cancelling, setCancelling] = useState(false)
  const [dispatching, setDispatching] = useState(false)
  const [next, setNext] = useState<SalesDispatchResultDto | null>(null)
  const [delivering, setDelivering] = useState<{ publicId: string; number: string } | null>(null)
  const open = order.status === 'REQUESTED' || order.status === 'PARTIAL'
  const canDispatch = open && (order.lines ?? []).some((l) => (l.qtyReserved ?? 0) > 0)
  const anyShipped = (order.lines ?? []).some((l) => (l.qtyShipped ?? 0) > 0)

  const run = async (p: Promise<unknown>, ok: string) => {
    try {
      await p
      toast.success(ok)
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const columns = useMemo<DataColumn<SalesOrderLineDto>[]>(
    () => [
      { id: 'product', header: t('sales.col.product'), cell: (l) => (<><b>{l.sku}</b><div className="help">{l.productName}</div></>), exportValue: (l) => l.sku ?? '', sortValue: (l) => l.sku ?? '', card: 'title' },
      { id: 'ordered', header: t('sales.col.ordered'), cell: (l) => f.number(l.qtyOrdered ?? 0), sortValue: (l) => l.qtyOrdered ?? 0, align: 'end' },
      { id: 'reserved', header: t('sales.col.reserved'), cell: (l) => f.number(l.qtyReserved ?? 0), sortValue: (l) => l.qtyReserved ?? 0, align: 'end' },
      {
        id: 'backordered',
        header: t('sales.col.backordered'),
        cell: (l) => ((l.qtyBackordered ?? 0) > 0 ? <Chip tone="warn">{f.number(l.qtyBackordered ?? 0)}</Chip> : l.isClosed ? <Chip>{t('sales.col.closed')}</Chip> : '—'),
        exportValue: (l) => l.qtyBackordered ?? 0,
        sortValue: (l) => l.qtyBackordered ?? 0,
        align: 'end',
      },
      { id: 'shipped', header: t('sales.col.shipped'), cell: (l) => f.number(l.qtyShipped ?? 0), sortValue: (l) => l.qtyShipped ?? 0, align: 'end' },
      { id: 'price', header: t('sales.col.price'), cell: (l) => f.money(l.unitPrice ?? 0), sortValue: (l) => l.unitPrice ?? 0, align: 'end' },
      {
        id: 'tax',
        header: t('sales.col.tax'),
        cell: (l) => (l.isTaxExempt ? t('sales.col.exempt') : f.money(l.taxAmount ?? 0)),
        exportValue: (l) => l.taxAmount ?? 0,
        sortValue: (l) => l.taxAmount ?? 0,
        align: 'end',
      },
      { id: 'amount', header: t('sales.col.amount'), cell: (l) => f.money(l.lineSubtotal ?? 0), sortValue: (l) => l.lineSubtotal ?? 0, align: 'end' },
    ],
    [t, f],
  )

  const dispatchColumns = useMemo<DataColumn<SalesDispatchInfoDto>[]>(
    () => [
      { id: 'number', header: t('sales.dispatches.number'), cell: (d) => <b>{d.number}</b>, exportValue: (d) => d.number ?? '', sortValue: (d) => d.number ?? '', card: 'title' },
      {
        id: 'status',
        header: t('sales.col.status'),
        cell: (d) => <Chip tone={d.status === 'PACKED' ? 'deliv' : d.status === 'CANCELLED' ? 'neutral' : 'warn'}>{t(`sales.dispatches.statuses.${d.status}`)}</Chip>,
        exportValue: (d) => t(`sales.dispatches.statuses.${d.status}`),
        sortValue: (d) => d.status ?? '',
      },
      { id: 'qty', header: t('sales.dispatches.quantity'), cell: (d) => f.number(d.quantity ?? 0), sortValue: (d) => d.quantity ?? 0, align: 'end' },
      { id: 'when', header: t('sales.dispatches.when'), cell: (d) => f.dateTime(d.collectedAtUtc), exportValue: (d) => d.collectedAtUtc ?? '', sortValue: (d) => d.collectedAtUtc ?? '' },
      { id: 'delivery', header: t('sales.dispatches.delivery'), cell: (d) => d.transportOrderNumber ?? '—', exportValue: (d) => d.transportOrderNumber ?? '', sortValue: (d) => d.transportOrderNumber ?? '' },
    ],
    [t, f],
  )

  const canChangeLine = (l: SalesOrderLineDto) => open && !l.isClosed && (l.qtyShipped ?? 0) === 0
  return (
    <Modal
      open
      size="lg"
      title={t('sales.view.title', { number: order.number ?? '' })}
      onClose={onClose}
      footer={
        <>
          {canDispatch && (
            <Can perm="warehouse.pick">
              <button type="button" className="btn flow" onClick={() => setDispatching(true)}>{t('sales.dispatch.button')}</button>
            </Can>
          )}
          {open && !anyShipped && (
            <Can perm="sales.cancel">
              <button type="button" className="btn danger" onClick={() => setCancelling(true)}>{t('sales.cancelOrder')}</button>
            </Can>
          )}
          <button type="button" className="btn flow" onClick={onClose}>{t('sales.close')}</button>
        </>
      }
    >
      <div className="pb sales-form">
        <p>
          <Chip color={order.statusColor ?? undefined}>{order.statusLabel}</Chip>{' '}
          {order.hasBackorder && <Chip tone="warn">{t('sales.withBackorder')}</Chip>}
        </p>
        <dl className="sales-facts">
          <div><dt>{t('sales.client')}</dt><dd>{order.clientName}</dd></div>
          <div><dt>{t('sales.warehouse')}</dt><dd>{order.warehouseName}</dd></div>
          <div><dt>{t('sales.requestedDate')}</dt><dd>{f.date(order.requestedDate)}{order.requestedByName ? ` · ${order.requestedByName}` : ''}</dd></div>
          <div><dt>{t('sales.requiredDate')}</dt><dd>{order.requiredDate ? f.date(order.requiredDate) : '—'}</dd></div>
          {order.notes && <div><dt>{t('sales.notes')}</dt><dd>{order.notes}</dd></div>}
        </dl>
        <DataTable
          label={t('sales.lines')}
          columns={columns}
          rows={order.lines ?? []}
          rowKey={(l) => String(l.id)}
          empty={t('sales.noLines')}
          rowActions={[
            { key: 'qty', label: t('sales.changeQty'), onClick: (l) => setEditing(l), perm: 'sales.create', visible: canChangeLine },
            {
              key: 'fulfill',
              label: t('sales.fulfill'),
              onClick: (l) => void run(fulfillLine.mutateAsync({ id: order.publicId!, lineId: l.id! }), t('sales.fulfilled', { sku: l.sku ?? '' })),
              perm: 'sales.backorder',
              visible: (l) => open && !l.isClosed && (l.qtyBackordered ?? 0) > 0,
            },
            {
              key: 'close',
              label: t('sales.closeLine'),
              onClick: (l) => void run(closeLine.mutateAsync({ id: order.publicId!, lineId: l.id! }), t('sales.lineClosed', { sku: l.sku ?? '' })),
              perm: 'sales.backorder',
              visible: (l) => open && !l.isClosed && (l.qtyBackordered ?? 0) > 0,
            },
          ]}
        />
        <p className="sales-totals" role="status">
          {t('sales.totals', { subtotal: f.money(order.subtotal ?? 0), tax: f.money(order.taxAmount ?? 0), total: f.money(order.total ?? 0) })}
        </p>
        {(order.dispatches ?? []).length > 0 && (
          <DataTable
            label={t('sales.dispatches.title')}
            columns={dispatchColumns}
            rows={order.dispatches ?? []}
            rowKey={(d) => d.pickBatchPublicId ?? ''}
            empty={t('sales.dispatches.empty')}
            rowActions={[
              { key: 'deliver', label: t('sales.dispatches.deliver'), perm: 'warehouse.pick', visible: (d) => d.status === 'COLLECTED', onClick: (d) => setDelivering({ publicId: d.pickBatchPublicId!, number: d.number ?? '' }) },
            ]}
          />
        )}
      </div>
      {editing && <QuantityModal order={order} line={editing} onClose={() => setEditing(null)} />}
      {dispatching && (
        <DispatchModal
          order={order}
          onClose={() => setDispatching(false)}
          onDone={(r) => {
            setDispatching(false)
            toast.success(t('sales.dispatch.done', { number: r.pickBatchNumber ?? '' }))
            setNext(r)
          }}
        />
      )}
      {next && (
        <NextDialog
          result={next}
          onPickup={() => setNext(null)}
          onLater={() => setNext(null)}
          onDelivery={() => {
            setDelivering({ publicId: next.pickBatchPublicId!, number: next.pickBatchNumber ?? '' })
            setNext(null)
          }}
        />
      )}
      {delivering && (
        <PackModal
          batch={{ publicId: delivering.publicId, number: delivering.number, clientName: null, rowVersion: null }}
          forSalesOrder={{ orderPublicId: order.publicId!, clientPublicId: order.clientPublicId!, consigneePublicId: order.consigneePublicId }}
          onClose={() => setDelivering(null)}
        />
      )}
      <ConfirmDialog
        open={cancelling}
        title={t('sales.cancelTitle', { number: order.number ?? '' })}
        message={t('sales.cancelRequestedBody')}
        confirmLabel={t('sales.cancelOrder')}
        tone="danger"
        onConfirm={async () => {
          await cancel.mutateAsync({ id: order.publicId! })
          toast.success(t('sales.cancelled', { number: order.number ?? '' }))
          setCancelling(false)
          onClose()
        }}
        onClose={() => setCancelling(false)}
      />
    </Modal>
  )
}

// ---------------------------------------------------------------- punto de entrada

/** `publicId` null = orden nueva. */
export function OrderModal({ publicId, onClose }: { publicId: string | null; onClose: () => void }) {
  const t = useT()
  const order = useSalesOrder(publicId)
  const canCreate = useCan('sales.create')
  if (publicId === null) return <DraftEditor order={null} onClose={onClose} />
  if (order.isPending || order.isError) {
    return (
      <Modal open size="lg" title={t('sales.loading')} onClose={onClose} footer={<button type="button" className="btn" onClick={onClose}>{t('sales.close')}</button>}>
        <div className="pb">{order.isError ? <p className="ferr" role="alert">{applyProblemDetails(order.error).title}</p> : <Spinner block />}</div>
      </Modal>
    )
  }
  // `key` solo por orden: al cambiar de estatus (despachar, cancelar) la ventana NO se reinicia (conserva el diálogo «¿qué sigue?» y la entrega)
  return order.data.status === 'DRAFT' && canCreate ? (
    <DraftEditor key={`${order.data.publicId}-edit`} order={order.data} onClose={onClose} />
  ) : (
    <OrderView key={`${order.data.publicId}-view`} order={order.data} onClose={onClose} />
  )
}
