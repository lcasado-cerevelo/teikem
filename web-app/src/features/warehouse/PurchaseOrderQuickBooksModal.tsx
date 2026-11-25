// Lote 246 (Lote F) — «QuickBooks» en Órdenes de compra (purchasing.quickbooks): las órdenes enviadas, recibidas en parte o recibidas que faltan por pasar a QuickBooks Desktop.
// «Exportar» saca las elegidas en un archivo .iif (PURCHORD; pregunta si van los proveedores y productos nuevos), lo baja y pregunta «¿Ya las importaste en QuickBooks?» con la lista marcada:
// «Sí, ya están» las marca, «No pasó» las deja como por pasar y «Todavía no» no cambia nada. «Marcar como en QuickBooks» las marca a mano. Las que vinieron de QuickBooks nunca salen aquí.
// Con `onlyId` (ficha de una orden) la lista se limita a esa orden. El formato PURCHORD todavía no se ha probado contra un QuickBooks real (ver el manual).
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, IconCheckCircle, Modal, QBox, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { usePurchaseQbMutations, usePurchaseQbPending, type PurchaseQbExportDto } from './purchaseQuickBooksApi'
import { downloadBase64File, filterPending, hasNewData, newDataOf, type PurchaseQbOrder, type PurchaseQbPending } from './purchaseQuickBooksRules'
import './purchaseQuickBooks.css'

const NO_ROWS: never[] = []

export function PurchaseOrderQuickBooksModal({ onClose, onlyId }: { onClose: () => void; onlyId?: string }) {
  const t = useT()
  const f = useFormat()
  const pending = usePurchaseQbPending()
  const { exportFile, confirm, notPassed, mark } = usePurchaseQbMutations()
  const [q, setQ] = useState('')
  const [unchecked, setUnchecked] = useState<Set<string>>(() => new Set())
  const [includeNew, setIncludeNew] = useState(true)
  const [exported, setExported] = useState<PurchaseQbExportDto | null>(null)
  const [markAll, setMarkAll] = useState(false)
  const [details, setDetails] = useState<PurchaseQbOrder | null>(null)
  const [error, setError] = useState<string | null>(null)

  const data = pending.data
  const all = useMemo(() => (data?.orders ?? NO_ROWS).filter((o) => !onlyId || o.publicId === onlyId), [data, onlyId])
  const rows = useMemo(() => filterPending(all, q), [all, q])
  // por omisión van todas marcadas; lo desmarcado se recuerda aparte (lo que llegue nuevo entra marcado)
  const live = useMemo(() => all.filter((o) => !unchecked.has(o.publicId ?? '')).map((o) => o.publicId!), [all, unchecked])
  const allShownChecked = rows.length > 0 && rows.every((r) => !unchecked.has(r.publicId ?? ''))
  const toggle = (id: string, on: boolean) =>
    setUnchecked((prev) => {
      const next = new Set(prev)
      if (on) next.delete(id)
      else next.add(id)
      return next
    })
  const toggleShown = (on: boolean) =>
    setUnchecked((prev) => {
      const next = new Set(prev)
      for (const r of rows) if (on) next.delete(r.publicId!)
      else next.add(r.publicId!)
      return next
    })

  const newCount = (data?.newSuppliers ?? []).length + (data?.newProducts ?? []).length
  const busy = exportFile.isPending || mark.isPending || confirm.isPending || notPassed.isPending
  const fail = (e: unknown) => setError(applyProblemDetails(e).title)

  const runExport = async () => {
    setError(null)
    try {
      // el total de las por pasar es el de la lista completa del servidor: con `onlyId` nunca coincide, así que va la lista de ids
      const dto = await exportFile.mutateAsync({ ids: live, totalPending: onlyId ? -1 : (data?.count ?? 0), includeNew })
      downloadBase64File(dto.contentBase64 ?? '', dto.fileName ?? 'teikem-quickbooks-compras.iif', dto.contentType)
      toast.success(t('purchaseQb.downloaded', { file: dto.fileName ?? '' }))
      setExported(dto)
    } catch (e) {
      fail(e)
    }
  }
  const runMark = async (ids: string[] | 'all') => {
    setError(null)
    try {
      const r = await mark.mutateAsync(ids)
      toast.success(t('purchaseQb.marked', { n: r.marked ?? 0 }))
      setUnchecked(new Set())
    } catch (e) {
      fail(e)
    }
  }

  const columns = useMemo<DataColumn<PurchaseQbOrder>[]>(
    () => [
      {
        id: 'select',
        header: t('purchaseQb.col.select'),
        exportable: false,
        cell: (r) => (
          <input type="checkbox" className="pqb-check" checked={!unchecked.has(r.publicId ?? '')} onChange={(e) => toggle(r.publicId!, e.target.checked)} aria-label={t('purchaseQb.selectRow', { number: r.number ?? '' })} />
        ),
      },
      { id: 'date', header: t('purchaseQb.col.date'), cell: (r) => (r.orderDate ? f.date(r.orderDate) : '—'), sortValue: (r) => r.orderDate ?? '' },
      { id: 'number', header: t('purchaseQb.col.number'), card: 'title', cell: (r) => <b>{r.number}</b>, sortValue: (r) => r.number ?? '' },
      {
        id: 'supplier',
        header: t('purchaseQb.col.supplier'),
        cell: (r) => (
          <>
            {r.supplierName}
            {r.quickBooksVendor && r.quickBooksVendor !== r.supplierName && <span className="pqb-who">{t('purchaseQb.inQuickBooksAs', { name: r.quickBooksVendor })}</span>}
          </>
        ),
        sortValue: (r) => r.supplierName ?? '',
      },
      { id: 'total', header: t('purchaseQb.col.total'), cell: (r) => f.money(r.total ?? 0), sortValue: (r) => r.total ?? 0, align: 'end' },
      {
        id: 'news',
        header: t('purchaseQb.col.news'),
        cell: (r) =>
          hasNewData(r) ? (
            <span className="pqb-news">
              {r.newSupplier && <Chip tone="warn">{t('purchaseQb.newSupplier')}</Chip>}
              {(r.newProductSkus ?? []).length > 0 && (
                <Chip tone="warn" title={(r.newProductSkus ?? []).join(', ')}>
                  {(r.newProductSkus ?? []).length === 1 ? t('purchaseQb.newProduct') : t('purchaseQb.newProducts', { n: (r.newProductSkus ?? []).length })}
                </Chip>
              )}
              <button type="button" className="linklike" onClick={() => setDetails(r)} aria-label={t('purchaseQb.seeDataOf', { number: r.number ?? '' })}>
                {t('purchaseQb.seeData')}
              </button>
            </span>
          ) : (
            '—'
          ),
        sortValue: (r) => (r.newSupplier ? 1 : 0) + (r.newProductSkus ?? []).length,
      },
      {
        id: 'exported',
        header: t('purchaseQb.col.exported'),
        cell: (r) =>
          r.exportedAtUtc ? (
            <>
              <Chip tone="cap">{t('purchaseQb.exportedOn', { date: f.date(r.exportedAtUtc) })}</Chip>
              {r.exportedByName && <span className="pqb-who">{t('purchaseQb.by', { name: r.exportedByName })}</span>}
            </>
          ) : (
            '—'
          ),
        sortValue: (r) => r.exportedAtUtc ?? '',
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, unchecked],
  )

  const already = data?.alreadyExportedCount ?? 0
  const footer = exported ? null : (
    <>
      <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('purchaseQb.close')}</button>
      <button type="button" className="btn" disabled={live.length === 0 || busy} onClick={() => void runMark(live)}>{t('purchaseQb.markSelected', { n: live.length })}</button>
      <button type="button" className="btn flow" disabled={live.length === 0 || busy} onClick={() => void runExport()}>
        {exportFile.isPending ? t('purchaseQb.exporting') : t('purchaseQb.export', { n: live.length })}
      </button>
    </>
  )

  return (
    <Modal open size="xl" title={t('purchaseQb.title')} onClose={onClose} dismissible={!busy} footer={footer}>
      {exported ? (
        <ExportedStep
          dto={exported}
          busy={busy}
          onLater={() => setExported(null)}
          onConfirm={async (ids) => {
            const r = await confirm.mutateAsync(ids)
            toast.success(t('purchaseQb.marked', { n: r.marked ?? 0 }))
            setExported(null)
          }}
          onNotPassed={async (ids) => {
            const r = await notPassed.mutateAsync(ids)
            toast.success(t('purchaseQb.notPassedDone', { n: r.reset ?? 0 }))
            setExported(null)
          }}
        />
      ) : pending.isError ? (
        <p className="ferr" role="alert">{applyProblemDetails(pending.error).title}</p>
      ) : pending.isPending ? (
        <Spinner block />
      ) : (
        <>
          <p className="help">{t('purchaseQb.help')}</p>
          {already > 0 && (
            <p className="note pqb-note" role="status">
              {t(already === 1 ? 'purchaseQb.alreadyExportedOne' : 'purchaseQb.alreadyExported', { n: already, date: data?.lastExportedAtUtc ? f.dateTime(data.lastExportedAtUtc) : '' })}
            </p>
          )}
          {(data?.warnings ?? []).length > 0 && (
            <div className="note pqb-note pqb-warn" role="status">
              <b>{t('purchaseQb.warningsTitle')}</b>
              <ul>
                {(data?.warnings ?? []).map((w) => (
                  <li key={w}>{w}</li>
                ))}
              </ul>
            </div>
          )}
          <div className="pqb-tools">
            {!onlyId && <QBox value={q} onChange={setQ} placeholder={t('purchaseQb.search')} />}
            <label className="pqb-all">
              <input type="checkbox" checked={allShownChecked} disabled={rows.length === 0} onChange={(e) => toggleShown(e.target.checked)} />
              <span>{t('purchaseQb.selectAll', { n: rows.length })}</span>
            </label>
            {!onlyId && (
              <button type="button" className="btn sm" disabled={(data?.count ?? 0) === 0 || busy} onClick={() => setMarkAll(true)}>{t('purchaseQb.markAll')}</button>
            )}
          </div>
          {newCount > 0 && (
            <>
              <label className="pqb-include">
                <input type="checkbox" checked={includeNew} onChange={(e) => setIncludeNew(e.target.checked)} />
                <span>{t('purchaseQb.includeNew')}</span>
              </label>
              <p className="help">{t('purchaseQb.includeNewHelp', { suppliers: (data?.newSuppliers ?? []).length, products: (data?.newProducts ?? []).length })}</p>
            </>
          )}
          {error && <p className="ferr" role="alert">{error}</p>}
          {/* la lista puede ser larga: se desplaza dentro del diálogo para que Exportar y Marcar queden siempre a la vista */}
          <div className="pqb-scroll">
            <DataTable
              label={t('purchaseQb.title')}
              columns={columns}
              rows={rows}
              rowKey={(r) => r.publicId ?? ''}
              defaultSort={{ id: 'date', desc: false }}
              empty={q ? t('purchaseQb.noMatch') : t('purchaseQb.empty')}
            />
          </div>
        </>
      )}
      <ConfirmDialog
        open={markAll}
        title={t('purchaseQb.markAllTitle')}
        message={t('purchaseQb.markAllBody', { n: data?.count ?? 0 })}
        confirmLabel={t('purchaseQb.markAll')}
        onConfirm={async () => {
          await runMark('all')
          setMarkAll(false)
        }}
        onClose={() => setMarkAll(false)}
      />
      {details && <NewDataModal order={details} pending={data} onClose={() => setDetails(null)} />}
    </Modal>
  )
}

/** Después de bajar el archivo: «¿Ya las importaste en QuickBooks?» con la lista marcada. Las desmarcadas no se tocan. */
function ExportedStep({ dto, busy, onLater, onConfirm, onNotPassed }: { dto: PurchaseQbExportDto; busy: boolean; onLater: () => void; onConfirm: (ids: string[]) => Promise<void>; onNotPassed: (ids: string[]) => Promise<void> }) {
  const t = useT()
  const f = useFormat()
  const orders = dto.orders ?? NO_ROWS
  const [checked, setChecked] = useState<Set<string>>(() => new Set(orders.map((o) => o.publicId!)))
  const [error, setError] = useState<string | null>(null)
  const ids = orders.filter((o) => checked.has(o.publicId ?? '')).map((o) => o.publicId!)
  const run = async (fn: (ids: string[]) => Promise<void>) => {
    setError(null)
    try {
      await fn(ids)
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <section aria-label={t('purchaseQb.ask.title', { n: orders.length })}>
      <h3 className="pqb-ask-title">{t('purchaseQb.ask.title', { n: orders.length })}</h3>
      <p className="pqb-summary">
        <b>{dto.fileName}</b> · {t('purchaseQb.ask.summary', { n: dto.orderCount ?? orders.length, total: f.money(dto.total ?? 0) })} ·{' '}
        {dto.includedNew ? t('purchaseQb.ask.includedNew', { suppliers: dto.newSupplierCount ?? 0, products: dto.newProductCount ?? 0 }) : t('purchaseQb.ask.withoutNew')}
      </p>
      {(dto.warnings ?? []).length > 0 && (
        <div className="note pqb-note pqb-warn" role="status">
          <b>{t('purchaseQb.warningsTitle')}</b>
          <ul>
            {(dto.warnings ?? []).map((w) => (
              <li key={w}>{w}</li>
            ))}
          </ul>
        </div>
      )}
      {(dto.alreadyExportedCount ?? 0) > 0 && (
        <p className="note pqb-note">{t('purchaseQb.ask.alreadyBefore', { n: dto.alreadyExportedCount ?? 0, date: dto.lastExportedAtUtc ? f.dateTime(dto.lastExportedAtUtc) : '' })}</p>
      )}
      <p className="help">{t('purchaseQb.ask.help')}</p>
      <label className="pqb-all">
        <input type="checkbox" checked={orders.length > 0 && ids.length === orders.length} onChange={(e) => setChecked(e.target.checked ? new Set(orders.map((o) => o.publicId!)) : new Set())} />
        <span>{t('purchaseQb.ask.checkAll', { n: orders.length })}</span>
      </label>
      <ul className="pqb-list" aria-label={t('purchaseQb.ask.listLabel')}>
        {orders.map((o) => (
          <li key={o.publicId}>
            <label>
              <input
                type="checkbox"
                checked={checked.has(o.publicId ?? '')}
                onChange={(e) =>
                  setChecked((prev) => {
                    const next = new Set(prev)
                    if (e.target.checked) next.add(o.publicId!)
                    else next.delete(o.publicId!)
                    return next
                  })
                }
              />
              <span className="pqb-num">{o.number}</span>
              <span className="pqb-sup">{o.supplierName}</span>
              <span className="mono pqb-total">{f.money(o.total ?? 0)}</span>
            </label>
          </li>
        ))}
      </ul>
      {error && <p className="ferr" role="alert">{error}</p>}
      <div className="act" style={{ justifyContent: 'flex-end', flexWrap: 'wrap' }}>
        <button type="button" className="btn" onClick={() => downloadBase64File(dto.contentBase64 ?? '', dto.fileName ?? 'teikem-quickbooks-compras.iif', dto.contentType)}>{t('purchaseQb.ask.again')}</button>
        <button type="button" className="btn" disabled={busy} onClick={onLater}>{t('purchaseQb.ask.later')}</button>
        <button type="button" className="btn danger" disabled={ids.length === 0 || busy} onClick={() => void run(onNotPassed)}>{t('purchaseQb.ask.notPassed', { n: ids.length })}</button>
        <button type="button" className="btn flow" disabled={ids.length === 0 || busy} onClick={() => void run(onConfirm)}>
          <IconCheckCircle /> {t('purchaseQb.ask.confirm', { n: ids.length })}
        </button>
      </div>
    </section>
  )
}

/** «ver datos»: el proveedor y los productos nuevos de una orden, tal como salen en el archivo. */
function NewDataModal({ order, pending, onClose }: { order: PurchaseQbOrder; pending: PurchaseQbPending | undefined; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const { supplier, products } = newDataOf(order, pending)
  const fact = (label: string, value: string | null | undefined) => (
    <div key={label}>
      <dt>{label}</dt>
      <dd>{value && value.trim() ? value : '—'}</dd>
    </div>
  )
  return (
    <Modal open title={t('purchaseQb.newData.title', { number: order.number ?? '' })} onClose={onClose} footer={<button type="button" className="btn" onClick={onClose}>{t('purchaseQb.close')}</button>}>
      {supplier && (
        <section className="pqb-data">
          <h3>{t('purchaseQb.newSupplier')}</h3>
          <dl className="pqb-facts">
            {fact(t('purchaseQb.newData.qbName'), supplier.quickBooksName)}
            {fact(t('purchaseQb.newData.name'), supplier.name)}
            {fact(t('purchaseQb.newData.contact'), supplier.contactName)}
            {fact(t('purchaseQb.newData.phone'), supplier.phone)}
            {fact(t('purchaseQb.newData.email'), supplier.email)}
            {fact(t('purchaseQb.newData.terms'), supplier.quickBooksTerms ?? (supplier.paymentTerm ? t('purchaseQb.newData.termsMissing', { code: supplier.paymentTerm }) : null))}
          </dl>
        </section>
      )}
      {products.length > 0 && (
        <section className="pqb-data">
          <h3>{t('purchaseQb.newData.products')}</h3>
          <ul className="pqb-products">
            {products.map((p) => (
              <li key={p.publicId}>
                <dl className="pqb-facts">
                  {fact(t('purchaseQb.newData.qbItem'), p.quickBooksName)}
                  {fact(t('purchaseQb.newData.productName'), p.name)}
                  {fact(t('purchaseQb.newData.cost'), p.cost != null ? f.money(p.cost) : null)}
                </dl>
              </li>
            ))}
          </ul>
        </section>
      )}
      {!supplier && products.length === 0 && <p className="help">{t('purchaseQb.newData.none')}</p>}
    </Modal>
  )
}
