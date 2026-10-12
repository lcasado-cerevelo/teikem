// Ajuste múltiple AJM-###### (2026-10-12; docs/ajuste-multiple-diseno.md). `/warehouse/multi-adjustment`, permiso `inventory.adjust.multi` (no viene
// en ningún rol: el administrador lo da desde Roles) + WMS_LOTSERIAL. Dos pestañas:
// - Nuevo ajuste: almacén → agregar productos (cada uno trae TODAS sus posiciones con existencia y la cantidad del sistema) → escribir «Hay ahora»
//   por posición (vacío = no se toca; igual = sin movimiento) → nota obligatoria → Aplicar (confirmación con el resumen). Todo o nada. Si el sistema
//   cambió mientras se contaba, el API responde 409 con las líneas: se marcan, se actualiza «En sistema» y se pide confirmar de nuevo.
// - Historial: pasadas anteriores y su detalle (En sistema / Hay ahora / Diferencia por línea).
// Productos con serie no entran (se ajustan con el ajuste normal). La lógica pura está en multiAdjust.ts.
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ApiError, applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n'
import { ConfirmDialog, DataTable, Modal, Panel, Spinner, Tabs, toast, type DataColumn } from '../../kernel/ui'
import './multiAdjust.css'
import { useWarehouses } from './api'
import { formatNumber } from './lineRules'
import {
  applyBlocker,
  blockFromStock,
  buildRequest,
  errorKey,
  generalErrors,
  isDirty,
  isSerial,
  MULTI_NOTES_MAX,
  parseCounted,
  refreshBlocks,
  rowDifference,
  rowErrors,
  summarize,
  type PositionRow,
  type ProductBlock,
  withAddedPositions,
} from './multiAdjust'
import {
  fetchMultiAdjustStock,
  useCreateMultiAdjustment,
  useMultiAdjustment,
  useMultiAdjustments,
  type MultiAdjustmentDetailDto,
  type MultiAdjustmentDto,
} from './multiAdjustApi'
import { BinSearchSelect, ProductSearchSelect, WarehousePicker, type ProductFilterItem } from './pickers'
import type { BinFilterItem } from './kardexView'
import { matchesQ } from '../../kernel/ui/matchesQ'

const T = 'warehouse.multiAdjust'

type Tab = 'new' | 'history'

export default function MultiAdjustScreen() {
  const t = useT()
  const [tab, setTab] = useState<Tab>('new')
  const [openDoc, setOpenDoc] = useState<string | null>(null)
  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t(`${T}.title`)}</h1>
          <p>{t(`${T}.subtitle`)}</p>
        </div>
      </div>
      <div style={{ marginBottom: 14 }}>
        <Tabs<Tab>
          label={t(`${T}.title`)}
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'new', label: t(`${T}.tabNew`) },
            { key: 'history', label: t(`${T}.tabHistory`) },
          ]}
        />
      </div>
      {/* la pestaña nueva se queda montada: lo escrito no se pierde al mirar el historial */}
      <div hidden={tab !== 'new'}>
        <NewAdjustment
          onDone={(publicId) => {
            setTab('history')
            setOpenDoc(publicId)
          }}
        />
      </div>
      {tab === 'history' && <HistoryTab />}
      <DetailModal publicId={openDoc} onClose={() => setOpenDoc(null)} />
    </div>
  )
}

// ---------------------------------------------------------------------------------------------------------------------
// Nuevo ajuste
// ---------------------------------------------------------------------------------------------------------------------

function NewAdjustment({ onDone }: { onDone: (publicId: string) => void }) {
  const t = useT()
  const lang = useLang()
  const fmt = useCallback((n: number) => formatNumber(n, lang), [lang])
  const { data: warehouses = [] } = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  const [chosenWarehouse, setWarehouse] = useState<string | null>(null)
  const [blocks, setBlocks] = useState<ProductBlock[]>([])
  const [notes, setNotes] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [topErrors, setTopErrors] = useState<string[]>([])
  const [loadingProduct, setLoadingProduct] = useState(false)
  const [skipped, setSkipped] = useState<string | null>(null)
  const [confirming, setConfirming] = useState(false)
  const create = useCreateMultiAdjustment()

  // con un solo almacén activo se elige solo
  const warehouse = chosenWarehouse ?? (warehouses.length === 1 ? (warehouses[0].publicId ?? null) : null)

  const summary = useMemo(() => summarize(blocks), [blocks])
  const blocker = applyBlocker(blocks, notes, warehouse)

  // salir con algo escrito avisa
  const dirty = isDirty(blocks, notes)
  useEffect(() => {
    if (!dirty) return
    const h = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', h)
    return () => window.removeEventListener('beforeunload', h)
  }, [dirty])

  const changeWarehouse = (next: string | null) => {
    if (next === warehouse) return
    setWarehouse(next)
    setBlocks([])
    setErrors({})
    setTopErrors([])
  }

  /**
   * «Agregar producto» es un desplegable con búsqueda y selección múltiple (marcar «Todos» lo que muestra la búsqueda): lo marcado es lo que
   * está en el documento; marcar agrega el bloque con sus posiciones y desmarcar lo quita.
   */
  const inDocument = useMemo<ProductFilterItem[]>(
    () => blocks.map((b) => ({ publicId: b.productPublicId, sku: b.sku, label: `${b.sku} · ${b.name}` })),
    [blocks],
  )
  const setProducts = async (items: ProductFilterItem[]) => {
    if (!warehouse) return
    const wanted = new Set(items.map((i) => i.publicId))
    const added = items.map((i) => i.publicId).filter((id) => !blocks.some((b) => b.productPublicId === id))
    setBlocks((prev) => prev.filter((b) => wanted.has(b.productPublicId)))
    if (added.length === 0) return
    setLoadingProduct(true)
    setSkipped(null)
    try {
      const stock = await fetchMultiAdjustStock(warehouse, added)
      const serial = stock.filter((p) => isSerial({ trackingTypeCode: p.trackingTypeCode ?? '' }))
      if (serial.length > 0) setSkipped(serial.map((p) => p.sku ?? '').join(', '))
      setBlocks((prev) => [...prev, ...stock.filter((p) => !serial.includes(p)).map(blockFromStock)])
    } catch (err) {
      setTopErrors([applyProblemDetails(err).title])
    } finally {
      setLoadingProduct(false)
    }
  }

  const setCounted = (productPublicId: string, key: string, value: string) => {
    setBlocks((prev) => prev.map((b) => (b.productPublicId !== productPublicId ? b : { ...b, rows: b.rows.map((r) => (r.key === key ? { ...r, counted: value } : r)) })))
    setErrors((prev) => {
      const k = errorKey(productPublicId, key)
      if (!(k in prev)) return prev
      const { [k]: _gone, ...rest } = prev
      return rest
    })
  }

  const apply = async () => {
    if (!warehouse) return
    const built = buildRequest(warehouse, notes, blocks)
    setErrors({})
    setTopErrors([])
    try {
      const doc = await create.mutateAsync(built.body)
      toast.success(t(`${T}.applied`, { number: doc.header?.number ?? '' }))
      setBlocks([])
      setNotes('')
      onDone(doc.header?.publicId ?? '')
    } catch (err) {
      if (err instanceof ApiError && (err.status === 409 || err.status === 400)) {
        const byRow = rowErrors(err.errors, built.index)
        setErrors(byRow)
        setTopErrors([...generalErrors(err.errors), ...(Object.keys(byRow).length === 0 ? [err.title] : [])])
        if (err.code === 'stale_system_qty') {
          // el sistema cambió mientras se contaba: se actualiza «En sistema» y se confirma de nuevo
          try {
            const fresh = await fetchMultiAdjustStock(warehouse, blocks.map((b) => b.productPublicId))
            setBlocks((prev) => refreshBlocks(prev, fresh))
            setTopErrors((prev) => [...prev, t(`${T}.staleHelp`)])
          } catch {
            /* la lectura falló: quedan marcadas las líneas */
          }
        }
        return
      }
      setTopErrors([applyProblemDetails(err).title])
    }
  }

  const blockerText = blocker ? t(`${T}.blocked.${blocker}`) : null

  return (
    <div className="mja">
      <div className="mja-top">
        <div className="f">
          <label htmlFor="mja-wh">{t(`${T}.warehouse`)}</label>
          <WarehousePicker id="mja-wh" value={warehouse} onChange={(id) => changeWarehouse(id)} placeholder={t(`${T}.warehousePlaceholder`)} />
        </div>
        <ProductSearchSelect
          label={t(`${T}.addProduct`)}
          value={inDocument}
          onChange={(items) => void setProducts(items)}
          includeInactive
          disabled={!warehouse || loadingProduct}
          placeholder={warehouse ? t(`${T}.productPlaceholder`) : t(`${T}.chooseWarehouse`)}
        />
      </div>
      {/* 2026-10-12 (Luis): la nota va arriba, debajo de Almacén y Agregar producto; el pie solo lleva el resumen y Aplicar */}
      <div className="f mja-notes">
        <label htmlFor="mja-notes">
          {t(`${T}.notes`)} <small>({t(`${T}.reasonFixed`)})</small>
        </label>
        <textarea id="mja-notes" value={notes} maxLength={MULTI_NOTES_MAX} placeholder={t(`${T}.notesPlaceholder`)} onChange={(e) => setNotes(e.target.value)} />
      </div>
      <p className="mja-hint">{t(`${T}.hint`)}</p>
      {loadingProduct && <Spinner label={t(`${T}.loadingProduct`)} />}
      {skipped && (
        <p className="note ferr" role="alert">
          {t(`${T}.serialSkipped`, { sku: skipped })}
        </p>
      )}
      {topErrors.length > 0 && (
        <div className="mja-errors" role="alert">
          {topErrors.map((m, i) => (
            <div key={i}>{m}</div>
          ))}
        </div>
      )}

      {blocks.length === 0 && warehouse && !loadingProduct && <p className="note">{t(`${T}.empty`)}</p>}

      {blocks.map((b) => (
        <ProductBlockView
          key={b.productPublicId}
          block={b}
          warehouse={warehouse}
          errors={errors}
          fmt={fmt}
          onCounted={(key, v) => setCounted(b.productPublicId, key, v)}
          onRemove={() => setBlocks((prev) => prev.filter((x) => x.productPublicId !== b.productPublicId))}
          onAddedPositions={(items) =>
            setBlocks((prev) => prev.map((x) => (x.productPublicId === b.productPublicId ? withAddedPositions(x, items) : x)))
          }
        />
      ))}

      <div className="mja-foot">
        <div className="mja-foot-row">
          <div>
            <div className="mja-summary" aria-live="polite">
              {t(`${T}.summary`, { lines: summary.lines, products: summary.products, up: fmt(summary.up), down: fmt(summary.down) })}
            </div>
            {blockerText && <div className="mja-reason note">{blockerText}</div>}
          </div>
          <button type="button" className="btn flow" disabled={blocker !== null || create.isPending} onClick={() => setConfirming(true)}>
            {t(`${T}.apply`)}
          </button>
        </div>
      </div>

      <ConfirmDialog
        open={confirming}
        title={t(`${T}.confirmTitle`)}
        message={
          <>
            <p>{t(`${T}.confirmBody`, { lines: summary.lines, products: summary.products, up: fmt(summary.up), down: fmt(summary.down) })}</p>
            <p className="note">{t(`${T}.confirmNote`)}</p>
          </>
        }
        confirmLabel={t(`${T}.apply`)}
        onConfirm={apply}
        onClose={() => setConfirming(false)}
      />
    </div>
  )
}

function ProductBlockView({
  block,
  warehouse,
  errors,
  fmt,
  onCounted,
  onRemove,
  onAddedPositions,
}: {
  block: ProductBlock
  warehouse: string | null
  errors: Record<string, string>
  fmt: (n: number) => string
  onCounted: (key: string, value: string) => void
  onRemove: () => void
  onAddedPositions: (items: BinFilterItem[]) => void
}) {
  const t = useT()
  const hasLot = block.trackingTypeCode === 'LOT'
  const [filter, setFilter] = useState('')
  const shownRows = useMemo(() => block.rows.filter((r) => matchesQ(filter, r.binCode, r.lotNumber ?? '')), [block.rows, filter])
  // dos bloques uno al lado del otro (la primera mitad a la izquierda); con pocas posiciones, uno solo
  const half = shownRows.length > 6 ? Math.ceil(shownRows.length / 2) : shownRows.length
  const columns = [shownRows.slice(0, half), shownRows.slice(half)].filter((c) => c.length > 0)
  const renderRow = (r: PositionRow) => {
    const err = errors[errorKey(block.productPublicId, r.key)]
    const parsed = parseCounted(r.counted)
    const diff = rowDifference(r)
    return (
      <tr key={r.key} className={err ? 'mja-row-bad' : undefined}>
        <td className="ref">
          {r.binCode}
          {r.added && <span className="mja-new">{t(`${T}.added`)}</span>}
        </td>
        {hasLot && <td>{r.lotNumber ?? '—'}</td>}
        <td className="num mono">{fmt(r.systemQty)}</td>
        <td className="num">
          <input
            className="mja-count mono"
            inputMode="decimal"
            value={r.counted}
            aria-label={t(`${T}.countedFor`, { bin: r.binCode })}
            aria-invalid={parsed === 'invalid' || Boolean(err)}
            placeholder="—"
            onChange={(e) => onCounted(r.key, e.target.value)}
          />
          {parsed === 'invalid' && <span className="mja-rowerr">{t(`${T}.invalidCount`)}</span>}
          {err && <span className="mja-rowerr">{err}</span>}
        </td>
        <td className="num mono">
          <Difference value={diff} fmt={fmt} />
        </td>
      </tr>
    )
  }
  return (
    <section className="mja-block" aria-label={`${block.sku} ${block.name}`}>
      <div className="mja-block-head">
        <div className="mja-block-title">
          {block.sku}
          <small>{block.name}</small>
        </div>
        <div className="mja-tools">
          {block.rows.length > 6 && (
            <input
              type="search"
              className="mja-find"
              value={filter}
              placeholder={t(`${T}.findPosition`)}
              aria-label={t(`${T}.findPosition`)}
              onChange={(e) => setFilter(e.target.value)}
            />
          )}
          <button type="button" className="btn" onClick={onRemove} aria-label={t(`${T}.removeProduct`, { sku: block.sku })}>
            {t(`${T}.remove`)}
          </button>
        </div>
      </div>
      {block.rows.length === 0 ? (
        <p className="note mja-empty">{t(`${T}.noPositions`)}</p>
      ) : shownRows.length === 0 ? (
        <p className="note mja-empty">{t('ui.searchSelect.none')}</p>
      ) : (
        <div className="mja-cols">
          {columns.map((rows, i) => (
            <div className="tbl-wrap" key={i}>
              <table className="tbl mja-tbl" aria-label={`${block.sku} ${block.name}`}>
                <thead>
                  <tr>
                    <th>{t(`${T}.cols.position`)}</th>
                    {hasLot && <th>{t(`${T}.cols.lot`)}</th>}
                    <th className="num">{t(`${T}.cols.system`)}</th>
                    <th className="num">{t(`${T}.cols.counted`)}</th>
                    <th className="num">{t(`${T}.cols.difference`)}</th>
                  </tr>
                </thead>
                <tbody>{rows.map(renderRow)}</tbody>
              </table>
            </div>
          ))}
        </div>
      )}
      <div className="mja-add">
        {/* «Agregar posición»: desplegable con búsqueda y selección múltiple («Todos» marca lo que muestra la búsqueda, p. ej. todas las 01-) */}
        <div className="mja-bin">
          <BinSearchSelect
            label={t(`${T}.addPosition`)}
            value={block.rows.filter((r) => r.added).map((r) => ({ id: r.binId, label: r.binCode }))}
            onChange={onAddedPositions}
            warehousePublicIds={warehouse ? [warehouse] : undefined}
            placeholder={t(`${T}.addPositionPlaceholder`)}
          />
        </div>
      </div>
    </section>
  )
}

function Difference({ value, fmt }: { value: number | null; fmt: (n: number) => string }) {
  if (value === null) return <span className="mja-eq">—</span>
  if (value === 0) return <span className="mja-eq">=</span>
  return <span className={value > 0 ? 'mja-up' : 'mja-down'}>{`${value > 0 ? '+' : '−'}${fmt(Math.abs(value))}`}</span>
}

// ---------------------------------------------------------------------------------------------------------------------
// Historial
// ---------------------------------------------------------------------------------------------------------------------

const PAGE = 25

function HistoryTab() {
  const t = useT()
  const f = useFormat()
  const lang = useLang()
  const [page, setPage] = useState(1)
  const [open, setOpen] = useState<string | null>(null)
  const { data, isLoading, error } = useMultiAdjustments({ skip: (page - 1) * PAGE, take: PAGE })
  const rows = data?.items ?? []

  const columns = useMemo<DataColumn<MultiAdjustmentDto>[]>(
    () => [
      { id: 'number', header: t(`${T}.history.number`), cell: (d) => d.number, sortValue: (d) => d.id, card: 'title' },
      { id: 'date', header: t(`${T}.history.date`), cell: (d) => f.dateTime(d.createdAtUtc), sortValue: (d) => d.createdAtUtc },
      { id: 'warehouse', header: t(`${T}.history.warehouse`), cell: (d) => d.warehouseCode ?? '', sortValue: (d) => d.warehouseCode },
      { id: 'lines', header: t(`${T}.history.lines`), cell: (d) => formatNumber(d.lineCount ?? 0, lang), align: 'end', sortValue: (d) => d.lineCount },
      { id: 'products', header: t(`${T}.history.products`), cell: (d) => formatNumber(d.productCount ?? 0, lang), align: 'end', sortValue: (d) => d.productCount },
      { id: 'up', header: t(`${T}.history.up`), cell: (d) => `+${formatNumber(d.unitsUp ?? 0, lang)}`, align: 'end', sortValue: (d) => d.unitsUp },
      { id: 'down', header: t(`${T}.history.down`), cell: (d) => `−${formatNumber(d.unitsDown ?? 0, lang)}`, align: 'end', sortValue: (d) => d.unitsDown },
      { id: 'by', header: t(`${T}.history.by`), cell: (d) => d.createdByName ?? '', sortValue: (d) => d.createdByName },
      { id: 'notes', header: t(`${T}.history.notes`), cell: (d) => d.notes ?? '', card: 'hidden' },
    ],
    [t, f, lang],
  )

  return (
    <Panel title={t(`${T}.tabHistory`)}>
      {error ? (
        <p className="note ferr" role="alert">
          {error.message || t('errors.generic')}
        </p>
      ) : (
        <DataTable
          label={t(`${T}.tabHistory`)}
          columns={columns}
          rows={rows}
          rowKey={(d) => d.id ?? 0}
          defaultSort={{ id: 'number', desc: true }}
          loading={isLoading}
          empty={t(`${T}.history.empty`)}
          onRowClick={(d) => setOpen(d.publicId ?? null)}
          page={page}
          pageSize={PAGE}
          total={data?.total ?? 0}
          onPage={setPage}
        />
      )}
      <DetailModal publicId={open} onClose={() => setOpen(null)} />
    </Panel>
  )
}

function DetailModal({ publicId, onClose }: { publicId: string | null; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const lang = useLang()
  const navigate = useNavigate()
  const { data, isLoading, error } = useMultiAdjustment(publicId)
  if (!publicId) return null
  return (
    <Modal
      open
      size="lg"
      title={data?.header ? t(`${T}.detail.title`, { number: data.header.number ?? '' }) : t(`${T}.detail.loading`)}
      onClose={onClose}
      footer={
        <>
          {data?.header?.number && (
            <button type="button" className="btn" onClick={() => navigate(`/warehouse/transfers-adjustments?q=${encodeURIComponent(data.header?.number ?? '')}`)}>
              {t(`${T}.detail.openKardex`)}
            </button>
          )}
          <button type="button" className="btn flow" onClick={onClose}>
            {t(`${T}.close`)}
          </button>
        </>
      }
    >
      {isLoading && <Spinner />}
      {error && (
        <p className="note ferr" role="alert">
          {error.message || t('errors.generic')}
        </p>
      )}
      {data && <DetailBody data={data} f={f} lang={lang} />}
    </Modal>
  )
}

function DetailBody({ data, f, lang }: { data: MultiAdjustmentDetailDto; f: ReturnType<typeof useFormat>; lang: string }) {
  const t = useT()
  const h = data.header
  const fmt = (n: number | undefined) => formatNumber(n ?? 0, lang)
  return (
    <>
      <p>
        {[h?.warehouseCode, h?.createdAtUtc ? f.dateTime(h.createdAtUtc) : '', h?.createdByName].filter(Boolean).join(' · ')}
      </p>
      <p>
        <strong>{t(`${T}.history.notes`)}:</strong> {h?.notes}
      </p>
      <p className="mja-summary">{t(`${T}.summary`, { lines: h?.lineCount ?? 0, products: h?.productCount ?? 0, up: fmt(h?.unitsUp), down: fmt(h?.unitsDown) })}</p>
      <div className="tbl-wrap">
        <table className="tbl" aria-label={t(`${T}.detail.lines`)}>
          <thead>
            <tr>
              <th>{t(`${T}.cols.sku`)}</th>
              <th>{t(`${T}.cols.product`)}</th>
              <th>{t(`${T}.cols.position`)}</th>
              <th>{t(`${T}.cols.lot`)}</th>
              <th className="num">{t(`${T}.cols.system`)}</th>
              <th className="num">{t(`${T}.cols.counted`)}</th>
              <th className="num">{t(`${T}.cols.difference`)}</th>
            </tr>
          </thead>
          <tbody>
            {(data.lines ?? []).map((l, i) => (
              <tr key={l.inventoryTransactionId ?? i}>
                <td className="ref">{l.sku}</td>
                <td>{l.productName}</td>
                <td>{l.binCode}</td>
                <td>{l.lotNumber ?? '—'}</td>
                <td className="num mono">{fmt(l.systemQty)}</td>
                <td className="num mono">{fmt(l.countedQty)}</td>
                <td className="num mono">
                  <Difference value={l.difference ?? 0} fmt={(n) => formatNumber(n, lang)} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  )
}
