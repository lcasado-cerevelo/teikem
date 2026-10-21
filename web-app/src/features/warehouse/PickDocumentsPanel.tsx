// Lote 173 (C1) — «Recolección y empaque» por DOCUMENTO: una fila por aviso de salida, orden de venta o envío empacado con su avance (pedido, sacado, por sacar) y cuántas recolecciones tiene;
// al abrirla se ven sus recolecciones como movimientos (`PickDocumentModal`). Es la misma información de la lista de movimientos (`PickBatchesPanel`), agrupada: no cambia ningún dato.
// Filtros al API y a la página 1: Tipo, «Solo con pendiente por sacar» y el buscador libre (número o cliente). Tabla paginada por el servidor con `exportRows` (ordenar y exportar usan TODO el filtro).
import { useMemo, useRef, useState } from 'react'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, DataTable, Filters, IconDoc, Panel, QBox, SelectFilter, useElementWidth, type DataColumn } from '../../kernel/ui'
import { exportPickDocuments, usePickDocuments, type PickDocumentDto } from './api'
import { ToggleFilter } from './filterControls'
import { formatDateTime, useDebounced } from './lineRules'
import { PickDocumentModal } from './PickDocumentModal'
import { documentProgress } from './pickDocumentView'
import './warehouse.css'

const PAGE_SIZE = 25
const NO_ROWS: never[] = []
/** Bajo este ancho del panel la tabla pasa a tarjetas. */
const CARDS_BELOW_PX = 640

export function PickDocumentsPanel({ highlight }: { highlight?: string | null }) {
  const t = useT()
  const lang = useLang()
  const boxRef = useRef<HTMLDivElement>(null)
  const width = useElementWidth(boxRef)
  const [kind, setKind] = useState('')
  const [pendingOnly, setPendingOnly] = useState(false)
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)
  const [open, setOpen] = useState<PickDocumentDto | null>(null)
  const search = useDebounced(q.trim())

  function reset<T>(setter: (v: T) => void) {
    return (v: T) => {
      setPage(1)
      setter(v)
    }
  }

  const query = useMemo(
    () => ({ kind: kind || undefined, pendingOnly: pendingOnly || undefined, search: search || undefined, skip: (page - 1) * pageSize, take: pageSize }),
    [kind, pendingOnly, search, page, pageSize],
  )
  const { data, isLoading, error } = usePickDocuments(query)

  const columns = useMemo<DataColumn<PickDocumentDto>[]>(
    () => [
      {
        id: 'number',
        header: t('warehouse.pickBatches.docs.columns.document'),
        cell: (d) => (
          <span className="collect-num">
            <span className="ref">{d.number}</span>
            <Chip tone="cap">{t(`warehouse.pickBatches.docs.kinds.${d.kind}`)}</Chip>
          </span>
        ),
        sortValue: (d) => d.number,
        exportValue: (d) => `${d.number} · ${t(`warehouse.pickBatches.docs.kinds.${d.kind}`)}`,
        card: 'title',
      },
      { id: 'client', header: t('warehouse.pickBatches.docs.columns.client'), cell: (d) => d.clientName ?? '—', sortValue: (d) => d.clientName ?? '' },
      { id: 'status', header: t('warehouse.pickBatches.docs.columns.status'), cell: (d) => <Chip>{d.status}</Chip>, sortValue: (d) => d.status ?? '', exportValue: (d) => d.status ?? '' },
      {
        id: 'progress',
        header: t('warehouse.pickBatches.docs.columns.progress'),
        cell: (d) => {
          const p = documentProgress(d)
          return (
            <span className="pdoc-progress">
              <span>{t(p.ordered == null ? 'warehouse.pickBatches.docs.shippedOnly' : 'warehouse.pickBatches.docs.shippedOf', { shipped: p.shipped, ordered: p.ordered ?? 0 })}</span>
              {p.percent != null && <progress max={100} value={p.percent} aria-label={t('warehouse.pickBatches.docs.percent', { n: p.percent })} />}
              {p.toCollect > 0 && <Chip tone="warn">{t('warehouse.pickBatches.docs.toCollect', { n: p.toCollect })}</Chip>}
              {p.unreserved > 0 && <Chip>{t('warehouse.pickBatches.docs.unreserved', { n: p.unreserved })}</Chip>}
            </span>
          )
        },
        sortValue: (d) => documentProgress(d).percent ?? -1,
        exportValue: (d) => {
          const p = documentProgress(d)
          return p.ordered == null ? `${p.shipped}` : `${p.shipped}/${p.ordered}`
        },
      },
      { id: 'moves', header: t('warehouse.pickBatches.docs.columns.moves'), cell: (d) => d.batchCount ?? 0, sortValue: (d) => d.batchCount ?? 0, align: 'end' },
      { id: 'last', header: t('warehouse.pickBatches.docs.columns.last'), cell: (d) => (d.lastCollectedAtUtc ? formatDateTime(d.lastCollectedAtUtc, lang) : '—'), sortValue: (d) => d.lastCollectedAtUtc ?? '' },
    ],
    [t, lang],
  )

  return (
    <div ref={boxRef} className="collect-list">
      <Panel flush icon={<IconDoc />} title={t('warehouse.pickBatches.docs.title')} badge={data ? (data.total ?? 0) : undefined}>
        <div className="collect-filters">
          <Filters
            onClear={() => {
              setPage(1)
              setKind('')
              setPendingOnly(false)
              setQ('')
            }}
          >
            <SelectFilter
              label={t('warehouse.pickBatches.docs.filters.kind')}
              value={kind}
              onChange={reset(setKind)}
              options={['EXIT_NOTICE', 'SALES_ORDER', 'ORDER'].map((k) => ({ value: k, label: t(`warehouse.pickBatches.docs.kinds.${k}`) }))}
            />
            <ToggleFilter label={t('warehouse.pickBatches.docs.filters.pendingOnly')} checked={pendingOnly} onChange={reset(setPendingOnly)} />
          </Filters>
        </div>
        <div className="qrow">
          <QBox value={q} onChange={reset(setQ)} placeholder={t('warehouse.pickBatches.docs.searchPlaceholder')} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.pickBatches.docs.title')}
            columns={columns}
            rows={data?.items ?? NO_ROWS}
            rowKey={(d) => `${d.kind}:${d.publicId}`}
            loading={isLoading}
            page={page}
            pageSize={pageSize}
            total={data?.total ?? 0}
            onPage={setPage}
            onPageSize={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            exportRows={() => exportPickDocuments(query)}
            forceCards={width > 0 && width < CARDS_BELOW_PX}
            onRowClick={(d) => setOpen(d)}
            rowClassName={(d) => (highlight && d.batches?.some((b) => b.publicId === highlight) ? 'collect-new' : undefined)}
            empty={t('warehouse.pickBatches.docs.empty')}
          />
        )}
      </Panel>
      {open && <PickDocumentModal document={open} onClose={() => setOpen(null)} />}
    </div>
  )
}
